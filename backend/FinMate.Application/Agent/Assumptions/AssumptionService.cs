using System.Text.RegularExpressions;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;

namespace FinMate.Application.Agent;

/// <param name="CreatedBySystem">Do hệ thống soạn (CPI tự cập nhật, hoặc nháp từ giá thị trường), để trang duyệt gắn nhãn.</param>
public sealed record AssumptionDto(Guid Id, string Key, int RateBps, string Unit, string SourceName, string SourceUrl, DateTimeOffset AsOf, int Version, string Status, Guid CreatedBy, Guid? ApprovedBy, Guid? RejectedBy = null, string? DecisionNote = null, bool CreatedBySystem = false)
{
    public static AssumptionDto From(EconomicAssumption a)
        => new(a.Id, a.Key, a.RateBps, a.Unit, a.SourceName, a.SourceUrl, a.AsOf, a.Version, a.Status.ToString().ToLowerInvariant(), a.CreatedBy, a.ApprovedBy, a.RejectedBy, a.DecisionNote,
            a.CreatedBy == Admin.Commands.AiCategorySyncService.SystemActorId);
}

public interface IAssumptionService
{
    Task<EconomicAssumption> CreateDraftAsync(Guid actorId, string key, int rateBps, string unit, string sourceName, string sourceUrl, DateTimeOffset asOf, string? ipAddress, CancellationToken ct = default);
    Task<EconomicAssumption> ApproveAsync(Guid id, Guid approverId, string? ipAddress, CancellationToken ct = default);
    Task<EconomicAssumption> RetireAsync(Guid id, Guid actorId, string? ipAddress, CancellationToken ct = default);

    /// <summary>Phase 95: từ chối một bản nháp kèm ghi chú (3–1000 ký tự). Người từ chối khác người soạn.</summary>
    Task<EconomicAssumption> RejectAsync(Guid id, Guid actorId, string? note, string? ipAddress, CancellationToken ct = default);
    Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default);
}

/// <summary>
/// Vòng đời một giả định: <c>Draft → Approved → Retired</c>. Chỉ bản Approved mới nhất của một khóa được agent dùng. Mọi thao tác GHI để lại đúng một dòng audit_logs
/// (khóa, phiên bản, tỉ lệ — không có bí mật nào). Phase 95: người duyệt / từ chối phải khác người soạn (trừ job CPI, tài khoản hệ thống) và có quyền
/// <c>ApproveAgentContent</c> đọc từ DB; bản nháp có thể bị từ chối kèm ghi chú.
/// </summary>
public sealed partial class AssumptionService(IEconomicAssumptionRepository repository, IAuditLogService audit, IOpsAccessService? access = null) : IAssumptionService
{
    [GeneratedRegex("^[a-z][a-z0-9_]{2,47}$")]
    private static partial Regex KeyPattern();

    public async Task<EconomicAssumption> CreateDraftAsync(
        Guid actorId, string key, int rateBps, string unit, string sourceName, string sourceUrl, DateTimeOffset asOf, string? ipAddress, CancellationToken ct = default)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new BusinessRuleException("invalid_assumption_key", "Khóa giả định gồm chữ thường, số, gạch dưới; bắt đầu bằng chữ; 3–48 ký tự.");
        }

        if (rateBps < AgentLimits.AssumptionMinRateBps || rateBps > AgentLimits.AssumptionMaxRateBps)
        {
            throw new BusinessRuleException("invalid_assumption_rate", "Tỉ lệ nằm ngoài khoảng cho phép.");
        }

        if (string.IsNullOrWhiteSpace(unit) || unit.Length > 16)
        {
            throw new BusinessRuleException("invalid_assumption_unit", "Đơn vị là chuỗi 1–16 ký tự.");
        }

        RequireSource(sourceName, sourceUrl);

        var assumption = new EconomicAssumption
        {
            Id = Guid.NewGuid(),
            Key = key,
            RateBps = rateBps,
            Unit = unit.Trim(),
            SourceName = sourceName.Trim(),
            SourceUrl = sourceUrl.Trim(),
            AsOf = asOf,
            Version = await repository.GetMaxVersionAsync(key, ct) + 1,
            Status = AssumptionStatus.Draft,
            CreatedBy = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(assumption, ct);

        await audit.LogAsync(AuditEvents.AdminAssumptionCreated, actorId, ipAddress, new { assumption.Key, assumption.Version, assumption.RateBps }, ct);
        return assumption;
    }

    public async Task<EconomicAssumption> ApproveAsync(Guid id, Guid approverId, string? ipAddress, CancellationToken ct = default)
    {
        var assumption = await RequireDecidableAsync(id, approverId, ct);

        RequireSource(assumption.SourceName, assumption.SourceUrl); // phòng bản ghi nháp bị sửa ngoài luồng: không nguồn thì không bao giờ được duyệt

        var previous = await repository.GetApprovedAsync(assumption.Key, ct);
        if (previous is not null && previous.Id != assumption.Id)
        {
            previous.Status = AssumptionStatus.Retired;
        }

        assumption.Status = AssumptionStatus.Approved;
        assumption.ApprovedBy = approverId;
        await repository.SaveChangesAsync(ct);

        await audit.LogAsync(
            AuditEvents.AdminAssumptionApproved, approverId, ipAddress,
            new { assumption.Key, assumption.Version, assumption.RateBps, RetiredVersion = previous?.Version }, ct);
        return assumption;
    }

    public async Task<EconomicAssumption> RetireAsync(Guid id, Guid actorId, string? ipAddress, CancellationToken ct = default)
    {
        await RequireApproverAsync(actorId, ct);
        var assumption = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException("EconomicAssumption", id);
        if (assumption.Status is AssumptionStatus.Retired or AssumptionStatus.Rejected)
        {
            throw new ConflictException("assumption_already_retired", "Giả định này đã được gỡ.");
        }

        assumption.Status = AssumptionStatus.Retired;
        await repository.SaveChangesAsync(ct);

        await audit.LogAsync(AuditEvents.AdminAssumptionRetired, actorId, ipAddress, new { assumption.Key, assumption.Version }, ct);
        return assumption;
    }

    public Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default) => repository.ListAsync(key, ct);

    private static void RequireSource(string sourceName, string sourceUrl)
    {
        var validUrl = Uri.TryCreate(sourceUrl?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (string.IsNullOrWhiteSpace(sourceName) || !validUrl)
        {
            throw new BusinessRuleException("assumption_source_required", "Giả định phải có tên nguồn và đường dẫn http(s) tới nguồn.");
        }
    }

    public async Task<EconomicAssumption> RejectAsync(Guid id, Guid actorId, string? note, string? ipAddress, CancellationToken ct = default)
    {
        var clean = note?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length < MinNoteLength || clean.Length > MaxNoteLength)
        {
            throw new BusinessRuleException(AssumptionErrorCodes.NoteRequired, $"Cần ghi chú từ chối {MinNoteLength}–{MaxNoteLength} ký tự.");
        }

        var assumption = await RequireDecidableAsync(id, actorId, ct);
        assumption.Status = AssumptionStatus.Rejected;
        assumption.RejectedBy = actorId;
        assumption.RejectedAt = DateTimeOffset.UtcNow;
        assumption.DecisionNote = clean;
        await repository.SaveChangesAsync(ct);

        // Không ghi ghi chú vào audit (văn bản tự do); nó nằm ở chính dòng giả định.
        await audit.LogAsync(AuditEvents.AdminAssumptionRejected, actorId, ipAddress, new { assumption.Key, assumption.Version }, ct);
        return assumption;
    }

    private const int MinNoteLength = 3;
    private const int MaxNoteLength = 1000;

    /// <summary>Bản nháp, người quyết có quyền (đọc DB) và không phải người soạn — trừ job CPI: tài khoản hệ thống tự duyệt có chủ ý khi lệch nhỏ (Phase 73, D14).</summary>
    private async Task<EconomicAssumption> RequireDecidableAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        await RequireApproverAsync(actorId, ct);
        var assumption = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException("EconomicAssumption", id);
        if (assumption.Status != AssumptionStatus.Draft)
        {
            throw new ConflictException("assumption_not_draft", "Chỉ duyệt hoặc từ chối được bản nháp.");
        }

        if (assumption.CreatedBy == actorId && actorId != SystemActor)
        {
            throw new ForbiddenException(AssumptionErrorCodes.OwnDraft, "Bạn không thể tự duyệt hay từ chối giả định do chính mình soạn.");
        }

        return assumption;
    }

    /// <summary>Quyền duyệt đọc lại từ DB (D15): policy theo vai trò trong JWT vẫn còn ở controller, nhưng một người vừa bị gỡ quyền phải mất quyền ngay.</summary>
    private async Task RequireApproverAsync(Guid actorId, CancellationToken ct)
    {
        if (access is not null && actorId != SystemActor)
        {
            await access.RequireAsync(actorId, OpsCapability.ApproveAgentContent, ct);
        }
    }

    private static readonly Guid SystemActor = Admin.Commands.AiCategorySyncService.SystemActorId;
}

public static class AssumptionErrorCodes
{
    public const string OwnDraft = "ASSUMPTION_OWN_DRAFT";
    public const string NoteRequired = "ASSUMPTION_NOTE_REQUIRED";
}
