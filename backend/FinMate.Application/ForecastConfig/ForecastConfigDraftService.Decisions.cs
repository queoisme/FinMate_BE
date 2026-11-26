using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.ForecastConfig;

public sealed partial class ForecastConfigDraftService
{
    public async Task<ForecastConfigDraftDto> ApproveAsync(Guid actorId, Guid id, string? note, string? ip, CancellationToken ct = default)
    {
        var draft = await RequireDecidableAsync(actorId, id, ct);
        var active = await ActiveAsync(ct);
        if ((draft.BaseVersion ?? 0) != active.Version)
        {
            throw new ConflictException(ForecastConfigErrorCodes.Stale, "Bản nháp đã cũ: cấu hình đã có phiên bản mới sau khi bản này được soạn. Không tự gộp; người soạn cần làm lại.");
        }

        var sections = ForecastConfigSections.Deserialize(draft.Content);
        var full = sections.ApplyTo(active.Document);
        await ValidateOrThrowAsync(full, ct);
        draft.DecidedBy = actorId;
        draft.DecidedAt = Now();
        draft.DecisionNote = OptionalNote(note);
        draft.UpdatedAt = draft.DecidedAt;
        if (!await _repository.TryActivateDraftAsync(draft, System.Text.Json.JsonSerializer.Serialize(full, Json), ct))
        {
            throw new ConflictException(ForecastConfigErrorCodes.Stale, "Cấu hình vừa đổi hoặc bản nháp vừa được xử lý. Tải lại để xem trạng thái mới nhất.");
        }

        // Cùng việc của đường Admin (SystemConfigService.CommitAsync): bỏ bản đệm trên máy này (máy khác thấy trong vòng 10 giây) và ghi audit system-config.
        _provider.Invalidate();
        var diff = ForecastConfigDiff.Compute(ForecastConfigSections.Of(active.Document), sections);
        await AuditAsync(AuditEvents.OpsForecastConfigApproved, actorId, ip, draft, diff, ct);
        await _audit.LogAsync(
            AuditEvents.AdminSystemConfigChanged, actorId, ip,
            new { Action = "approve", Version = draft.Version, FromVersion = (int?)null, DraftId = draft.Id, ChangedSections = SystemConfigService.ChangedSections(active.Document, full) },
            ct);
        return await ToDtoAsync(draft, (draft.Version ?? 0, full), ct);
    }

    /// <summary>Từ chối cả bản đã cũ (S1): không cần bản mới nhất để nói "không".</summary>
    public async Task<ForecastConfigDraftDto> RejectAsync(Guid actorId, Guid id, string? note, string? ip, CancellationToken ct = default)
    {
        var draft = await RequireDecidableAsync(actorId, id, ct);
        draft.DecisionNote = RequireText(note, ForecastConfigErrorCodes.NoteRequired, "ghi chú từ chối");
        draft.Status = SystemConfigStatus.Rejected;
        draft.DecidedBy = actorId;
        draft.DecidedAt = Now();
        draft.UpdatedAt = draft.DecidedAt;
        await _repository.TrySaveDraftChangesAsync(ct);
        await AuditAsync(AuditEvents.OpsForecastConfigRejected, actorId, ip, draft, [], ct);
        return await ToDtoAsync(draft, await ActiveAsync(ct), ct);
    }

    /// <summary>Người duyệt có quyền (đọc DB), bản đang chờ duyệt, và không phải bản của chính mình (DB cũng chặn bằng CHECK).</summary>
    private async Task<SystemConfigVersion> RequireDecidableAsync(Guid actorId, Guid id, CancellationToken ct)
    {
        await _access.RequireAsync(actorId, OpsCapability.ApproveForecastConfig, ct);
        var draft = await LoadAsync(id, ct);
        if (draft.CreatedBy == actorId)
        {
            throw new ForbiddenException(ForecastConfigErrorCodes.OwnDraft, "Bạn không thể tự duyệt hay từ chối bản nháp của mình.");
        }

        RequireStatus(draft, SystemConfigStatus.PendingApproval);
        return draft;
    }

    private static string? OptionalNote(string? note)
    {
        var clean = note?.Trim();
        if (clean?.Length > MaxTextLength)
        {
            throw new BusinessRuleException(ForecastConfigErrorCodes.NoteRequired, $"Ghi chú tối đa {MaxTextLength} ký tự.");
        }

        return string.IsNullOrEmpty(clean) ? null : clean;
    }
}
