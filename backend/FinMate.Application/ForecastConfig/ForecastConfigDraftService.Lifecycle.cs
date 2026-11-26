using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.ForecastConfig;

public sealed partial class ForecastConfigDraftService
{
    public async Task<ForecastConfigDraftDto> CreateAsync(Guid actorId, CreateForecastConfigDraft command, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.DraftForecastConfig, ct);
        var reason = RequireText(command.Reason, ForecastConfigErrorCodes.ReasonRequired, "lý do");
        if (command.FromDraftId is not null && command.FromVersion is not null)
        {
            throw new BadRequestException(ForecastConfigErrorCodes.InvalidSource, "Chỉ chọn một trong fromDraftId và fromVersion.");
        }

        var active = await ActiveAsync(ct);
        var (sections, conflicts, replaced) = await StartingPointAsync(actorId, command, active, ct);
        if (command.Changes is { } changes)
        {
            sections = ForecastConfigPatch.Apply(sections, changes);
        }

        await ValidateOrThrowAsync(sections.ApplyTo(active.Document), ct);
        var now = Now();
        if (replaced is not null)
        {
            // S1: bản đang chờ đã cũ của chính người soạn được thay bằng bản này, trong cùng lần lưu (một giao dịch) để chỗ chờ duyệt được nhả ra.
            replaced.Status = SystemConfigStatus.Cancelled;
            replaced.UpdatedAt = now;
        }

        var draft = new SystemConfigVersion
        {
            Id = Guid.NewGuid(), Name = App, Scope = Scope, Status = SystemConfigStatus.Draft, BaseVersion = active.Version,
            Content = sections.Serialize(), Note = reason, CreatedBy = actorId, CreatedAt = now, UpdatedAt = now,
            Conflicts = conflicts.Count == 0 ? null : JsonSerializer.Serialize(conflicts, Json),
        };
        await _repository.AddDraftAsync(draft, ct);
        var diff = ForecastConfigDiff.Compute(ForecastConfigSections.Of(active.Document), sections);
        await AuditAsync(AuditEvents.OpsForecastConfigDraftCreated, actorId, ip, draft, diff, ct);
        if (replaced is not null)
        {
            await AuditAsync(AuditEvents.OpsForecastConfigCancelled, actorId, ip, replaced, [], ct);
        }

        return await ToDtoAsync(draft, active, ct);
    }

    /// <summary>Điểm bắt đầu của bản nháp mới: bản đang chạy, một phiên bản lịch sử (hoàn tác), hoặc phần đã đổi của một bản nháp cũ áp lên bản đang chạy (D9, S2).</summary>
    private async Task<(ForecastConfigSections Sections, IReadOnlyList<ForecastConfigConflict> Conflicts, SystemConfigVersion? Replaced)> StartingPointAsync(
        Guid actorId, CreateForecastConfigDraft command, (int Version, Admin.SystemConfig.SystemConfigDocument Document) active, CancellationToken ct)
    {
        var activeSections = ForecastConfigSections.Of(active.Document);
        if (command.FromVersion is { } version)
        {
            return (ForecastConfigSections.Of(await DocumentOfVersionAsync(version, ct)), [], null);
        }

        if (command.FromDraftId is not { } sourceId)
        {
            return (activeSections, [], null);
        }

        var source = await LoadAsync(sourceId, ct);
        var stale = (source.BaseVersion ?? 0) != active.Version;
        var replaced = SourceToReplace(source, actorId, stale);
        if (source.Status is SystemConfigStatus.Draft && !stale)
        {
            throw new BusinessRuleException(ForecastConfigErrorCodes.InvalidSource, "Bản nháp này còn mới: sửa trực tiếp thay vì làm lại.");
        }

        var sourceBase = ForecastConfigSections.Of(await DocumentOfVersionAsync(source.BaseVersion ?? 0, ct));
        var (sections, conflicts) = ForecastConfigDiff.Rebase(SectionsOf(source), sourceBase, activeSections);
        return (sections, conflicts, replaced);
    }

    /// <summary>Bản nguồn đang chờ duyệt chỉ "làm lại" được khi đã cũ và là của chính người soạn (S1); khi đó nó bị huỷ. Bản đã duyệt thì dùng <c>fromVersion</c>.</summary>
    private static SystemConfigVersion? SourceToReplace(SystemConfigVersion source, Guid actorId, bool stale)
    {
        switch (source.Status)
        {
            case SystemConfigStatus.PendingApproval when stale && source.CreatedBy == actorId:
                return source;
            case SystemConfigStatus.PendingApproval:
                throw new BusinessRuleException(ForecastConfigErrorCodes.InvalidSource, "Chỉ làm lại được bản chờ duyệt đã cũ của chính mình; bản còn mới thì rút lại rồi sửa.");
            case SystemConfigStatus.Active or SystemConfigStatus.Archived:
                throw new BusinessRuleException(ForecastConfigErrorCodes.InvalidSource, "Bản này đã được duyệt; dùng fromVersion để lấy lại giá trị của một phiên bản.");
            default:
                return null;
        }
    }

    public async Task<ForecastConfigDraftDto> UpdateAsync(Guid actorId, Guid id, UpdateForecastConfigDraft command, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.DraftForecastConfig, ct);
        var draft = await LoadAsync(id, ct);
        RequireAuthor(draft, actorId);
        RequireStatus(draft, SystemConfigStatus.Draft);
        if (command.ExpectedUpdatedAt is not { } expected || draft.UpdatedAt != expected.ToUniversalTime())
        {
            throw new ConflictException(ForecastConfigErrorCodes.DraftChanged, "Bản nháp vừa được sửa ở nơi khác. Tải lại rồi thử lại.");
        }

        var before = ForecastConfigSections.Deserialize(draft.Content);
        var after = ForecastConfigPatch.Apply(before, command.Changes);
        var active = await ActiveAsync(ct);
        await ValidateOrThrowAsync(after.ApplyTo(active.Document), ct);
        draft.Content = after.Serialize();
        draft.Note = command.Reason is null ? draft.Note : RequireText(command.Reason, ForecastConfigErrorCodes.ReasonRequired, "lý do");
        draft.UpdatedAt = Now();
        await _repository.TrySaveDraftChangesAsync(ct);
        await AuditAsync(AuditEvents.OpsForecastConfigDraftUpdated, actorId, ip, draft, ForecastConfigDiff.Compute(before, after), ct);
        return await ToDtoAsync(draft, active, ct);
    }

    public async Task<ForecastConfigDraftDto> SubmitAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.DraftForecastConfig, ct);
        var draft = await LoadAsync(id, ct);
        RequireAuthor(draft, actorId);
        RequireStatus(draft, SystemConfigStatus.Draft);
        var active = await ActiveAsync(ct);
        if ((draft.BaseVersion ?? 0) != active.Version)
        {
            throw new BusinessRuleException(ForecastConfigErrorCodes.Stale, "Bản nháp đã cũ: cấu hình vừa có phiên bản mới. Làm lại từ bản này rồi gửi duyệt.");
        }

        var sections = ForecastConfigSections.Deserialize(draft.Content);
        await ValidateOrThrowAsync(sections.ApplyTo(active.Document), ct);
        await RequireNoOtherPendingAsync(draft.Id, ct);
        draft.Status = SystemConfigStatus.PendingApproval;
        draft.SubmittedAt = Now();
        draft.UpdatedAt = draft.SubmittedAt;
        if (!await _repository.TrySaveDraftChangesAsync(ct))
        {
            await RequireNoOtherPendingAsync(draft.Id, ct);
            throw new ConflictException(ForecastConfigErrorCodes.PendingExists, "Đã có một bản tham số dự báo đang chờ duyệt.");
        }

        await AuditAsync(AuditEvents.OpsForecastConfigSubmitted, actorId, ip, draft, ForecastConfigDiff.Compute(ForecastConfigSections.Of(active.Document), sections), ct);
        return await ToDtoAsync(draft, active, ct);
    }

    /// <summary>D8: mỗi lúc chỉ một bản chờ duyệt; thông báo kèm id của bản đó để màn hình dẫn tới.</summary>
    private async Task RequireNoOtherPendingAsync(Guid draftId, CancellationToken ct)
    {
        if (await _repository.GetPendingAsync(App, Scope, ct) is { } pending && pending.Id != draftId)
        {
            throw new ConflictException(ForecastConfigErrorCodes.PendingExists, $"Đã có một bản tham số dự báo đang chờ duyệt ({pending.Id}).");
        }
    }

    public Task<ForecastConfigDraftDto> WithdrawAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default)
        => MoveByAuthorAsync(actorId, id, SystemConfigStatus.PendingApproval, SystemConfigStatus.Draft, AuditEvents.OpsForecastConfigWithdrawn, ip, ct);

    public Task<ForecastConfigDraftDto> CancelAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default)
        => MoveByAuthorAsync(actorId, id, SystemConfigStatus.Draft, SystemConfigStatus.Cancelled, AuditEvents.OpsForecastConfigCancelled, ip, ct);

    private async Task<ForecastConfigDraftDto> MoveByAuthorAsync(
        Guid actorId, Guid id, SystemConfigStatus from, SystemConfigStatus to, string auditEvent, string? ip, CancellationToken ct)
    {
        await _access.RequireAsync(actorId, OpsCapability.DraftForecastConfig, ct);
        var draft = await LoadAsync(id, ct);
        RequireAuthor(draft, actorId);
        RequireStatus(draft, from);
        draft.Status = to;
        draft.SubmittedAt = to == SystemConfigStatus.Draft ? null : draft.SubmittedAt;
        draft.UpdatedAt = Now();
        await _repository.TrySaveDraftChangesAsync(ct);
        await AuditAsync(auditEvent, actorId, ip, draft, [], ct);
        return await ToDtoAsync(draft, await ActiveAsync(ct), ct);
    }
}
