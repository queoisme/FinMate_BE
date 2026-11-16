using System.Text.RegularExpressions;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Agent;

public sealed record AssumptionDto(Guid Id, string Key, int RateBps, string Unit, string SourceName, string SourceUrl, DateTimeOffset AsOf, int Version, string Status, Guid CreatedBy, Guid? ApprovedBy)
{
    public static AssumptionDto From(EconomicAssumption a)
        => new(a.Id, a.Key, a.RateBps, a.Unit, a.SourceName, a.SourceUrl, a.AsOf, a.Version, a.Status.ToString().ToLowerInvariant(), a.CreatedBy, a.ApprovedBy);
}

public interface IAssumptionService
{
    Task<EconomicAssumption> CreateDraftAsync(Guid actorId, string key, int rateBps, string unit, string sourceName, string sourceUrl, DateTimeOffset asOf, string? ipAddress, CancellationToken ct = default);
    Task<EconomicAssumption> ApproveAsync(Guid id, Guid approverId, string? ipAddress, CancellationToken ct = default);
    Task<EconomicAssumption> RetireAsync(Guid id, Guid actorId, string? ipAddress, CancellationToken ct = default);
    Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default);
}

/// <summary>
/// Vòng đời một giả định: <c>Draft → Approved → Retired</c>. Chỉ bản Approved mới nhất của một khóa được agent dùng. Mọi thao tác GHI để lại đúng một dòng audit_logs
/// (khóa, phiên bản, tỉ lệ — không có bí mật nào). Quy tắc "người nhập khác người duyệt" thuộc phân quyền vai trò (Task 19), không nằm ở đây.
/// </summary>
public sealed partial class AssumptionService(IEconomicAssumptionRepository repository, IAuditLogService audit) : IAssumptionService
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
        var assumption = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException("EconomicAssumption", id);
        if (assumption.Status != AssumptionStatus.Draft)
        {
            throw new ConflictException("assumption_not_draft", "Chỉ duyệt được bản nháp.");
        }

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
        var assumption = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException("EconomicAssumption", id);
        if (assumption.Status == AssumptionStatus.Retired)
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
}
