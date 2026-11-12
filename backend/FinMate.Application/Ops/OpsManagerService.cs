using FinMate.Application.Admin;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Entities;

namespace FinMate.Application.Ops;

public record OpsDashboardDto(
    IReadOnlyList<OpsCount> OpenByStatus,
    IReadOnlyList<OpsCount> OpenByPriority,
    int Unassigned,
    int Overdue,
    IReadOnlyList<OpsAssigneeLoad> Load,
    int RecentDays,
    int RecentCount,
    IReadOnlyList<OpsCount> RecentByCategory,
    double? FirstResponseOnTimeRate,
    double? ResolvedOnTimeRate,
    int RatingCount,
    double? AverageRating,
    int PendingApprovals);

public record CannedReplyDto(Guid Id, string Title, string Body, bool IsActive, DateTimeOffset UpdatedAt);

public interface IOpsManagerService
{
    Task<OpsDashboardDto> GetDashboardAsync(Guid actorId, CancellationToken ct = default);
    Task<PagedResult<CannedReplyDto>> ListCannedRepliesAsync(Guid actorId, bool includeInactive, string? search, int offset, int limit, CancellationToken ct = default);
    Task<CannedReplyDto> CreateCannedReplyAsync(Guid actorId, string? title, string? body, string? ip, CancellationToken ct = default);
    Task<CannedReplyDto> UpdateCannedReplyAsync(Guid actorId, Guid id, string? title, string? body, bool? isActive, string? ip, CancellationToken ct = default);
    Task<AuditLogListDto> GetAuditLogsAsync(Guid actorId, string? eventType, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int limit, string? search = null, CancellationToken ct = default);
    Task<AiStatsDto> GetAiStatsAsync(Guid actorId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
}

public sealed class OpsManagerService : IOpsManagerService
{
    private const int RecentDays = 30;
    private const int MaxTitleLength = 200;
    private const string OpsEventPrefix = "Ops.";

    private readonly IOpsAccessService _access;
    private readonly IOpsRepository _ops;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IGetAiStatsQueryHandler _aiStats;
    private readonly IAuditLogService _audit;

    public OpsManagerService(IOpsAccessService access, IOpsRepository ops, IAuditLogRepository auditLogs, IGetAiStatsQueryHandler aiStats, IAuditLogService audit)
    {
        _access = access;
        _ops = ops;
        _auditLogs = auditLogs;
        _aiStats = aiStats;
        _audit = audit;
    }

    public async Task<OpsDashboardDto> GetDashboardAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ViewDashboard, ct);
        var now = DateTimeOffset.UtcNow;
        var d = await _ops.GetDashboardAsync(now, now.AddDays(-RecentDays), ct);
        return new OpsDashboardDto(
            d.OpenByStatus,
            d.OpenByPriority,
            d.Unassigned,
            d.Overdue,
            d.Load,
            RecentDays,
            d.RecentCount,
            d.RecentByCategory,
            Rate(d.RecentFirstRespondedInTime, d.RecentFirstResponded),
            Rate(d.RecentResolvedInTime, d.RecentResolved),
            d.RatingCount,
            d.AverageRating,
            d.PendingApprovals);
    }

    private static double? Rate(int part, int total) => total == 0 ? null : (double)part / total;

    public async Task<PagedResult<CannedReplyDto>> ListCannedRepliesAsync(Guid actorId, bool includeInactive, string? search, int offset, int limit, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var (skip, take) = AdminPaging.Normalize(offset, limit);
        var (items, total) = await _ops.ListCannedRepliesAsync(includeInactive, search, skip, take, ct);
        return new PagedResult<CannedReplyDto>(items.Select(ToDto).ToList(), total, skip, take);
    }

    public async Task<CannedReplyDto> CreateCannedReplyAsync(Guid actorId, string? title, string? body, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageCannedReplies, ct);
        var now = DateTimeOffset.UtcNow;
        var reply = new CannedReply
        {
            Id = Guid.NewGuid(),
            Title = SupportTicketRules.RequireText(title, MaxTitleLength, "tiêu đề"),
            Body = SupportTicketRules.RequireText(body, SupportTicketRules.MaxBodyLength, "nội dung"),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _ops.AddCannedReply(reply);
        await _ops.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsCannedReplyChanged, actorId, ip, new { cannedReplyId = reply.Id }, ct);
        return ToDto(reply);
    }

    public async Task<CannedReplyDto> UpdateCannedReplyAsync(Guid actorId, Guid id, string? title, string? body, bool? isActive, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageCannedReplies, ct);
        var reply = await _ops.GetCannedReplyAsync(id, ct) ?? throw new NotFoundException("CannedReply", id);
        if (title is not null)
        {
            reply.Title = SupportTicketRules.RequireText(title, MaxTitleLength, "tiêu đề");
        }

        if (body is not null)
        {
            reply.Body = SupportTicketRules.RequireText(body, SupportTicketRules.MaxBodyLength, "nội dung");
        }

        if (isActive is { } active)
        {
            reply.IsActive = active;
        }

        reply.UpdatedAt = DateTimeOffset.UtcNow;
        await _ops.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsCannedReplyChanged, actorId, ip, new { cannedReplyId = reply.Id }, ct);
        return ToDto(reply);
    }

    /// <summary>Chỉ sự kiện vận hành: nhật ký <c>Auth.*</c>, <c>Admin.*</c> có thông tin không thuộc phạm vi của Manager.</summary>
    public async Task<AuditLogListDto> GetAuditLogsAsync(Guid actorId, string? eventType, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int limit, string? search = null, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ViewOpsAudit, ct);
        if (eventType is not null && !eventType.StartsWith(OpsEventPrefix, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Chỉ xem được nhật ký vận hành.");
        }

        var result = await _auditLogs.GetPageAsync(
            new AuditLogListFilter(null, eventType, from, to, cursor, Math.Clamp(limit, 1, 100), OpsEventPrefix, search), ct);
        return new AuditLogListDto(
            result.Items.Select(a => new AuditLogDto(a.Id, a.UserId, a.EventType, a.Metadata, a.IpAddress, a.CreatedAt)).ToList(),
            result.NextCursor);
    }

    public async Task<AiStatsDto> GetAiStatsAsync(Guid actorId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ViewAiStats, ct);
        return await _aiStats.HandleAsync(new GetAiStatsQuery(from, to), ct);
    }

    private static CannedReplyDto ToDto(CannedReply c) => new(c.Id, c.Title, c.Body, c.IsActive, c.UpdatedAt);
}
