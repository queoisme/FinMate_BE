using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class OpsRepository : IOpsRepository
{
    private const string UniqueViolation = "23505";

    private readonly FinMateDbContext _context;

    public OpsRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task AddApprovalAsync(OpsApprovalRequest approval, CancellationToken ct = default)
    {
        _context.OpsApprovalRequests.Add(approval);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _context.Entry(approval).State = EntityState.Detached;
            throw new ConflictException(OpsErrorCodes.ApprovalAlreadyPending, "Đã có đề xuất đang chờ duyệt cho đối tượng này.");
        }
    }

    public Task<OpsApprovalRequest?> GetApprovalAsync(Guid id, CancellationToken ct = default)
        => _context.OpsApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<OpsApprovalRequest>> ListApprovalsAsync(OpsApprovalStatus? status, Guid? requestedBy, int limit, CancellationToken ct = default)
    {
        var query = _context.OpsApprovalRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        if (requestedBy is { } r)
        {
            query = query.Where(a => a.RequestedByUserId == r);
        }

        return query.OrderByDescending(a => a.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public Task<List<CannedReply>> ListCannedRepliesAsync(bool includeInactive, CancellationToken ct = default)
        => _context.CannedReplies.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Title)
            .ToListAsync(ct);

    public Task<CannedReply?> GetCannedReplyAsync(Guid id, CancellationToken ct = default)
        => _context.CannedReplies.FirstOrDefaultAsync(c => c.Id == id, ct);

    public void AddCannedReply(CannedReply reply) => _context.CannedReplies.Add(reply);

    public async Task<OpsDashboardData> GetDashboardAsync(DateTimeOffset now, DateTimeOffset recentFrom, CancellationToken ct = default)
    {
        var tickets = _context.SupportTickets.AsNoTracking();

        var open = tickets.Where(t => t.Status != SupportTicketStatus.Closed);
        var byStatus = await open.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byPriority = await open.GroupBy(t => t.Priority).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var unassigned = await open.CountAsync(t => t.AssigneeUserId == null, ct);

        var working = open.Where(t => t.Status != SupportTicketStatus.Resolved);
        var overdue = await working.CountAsync(t => t.ResolveDueAt < now || (t.FirstRespondedAt == null && t.FirstResponseDueAt < now), ct);

        var loadRows = await working
            .Where(t => t.AssigneeUserId != null)
            .GroupBy(t => t.AssigneeUserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                Open = g.Count(),
                Overdue = g.Count(t => t.ResolveDueAt < now || (t.FirstRespondedAt == null && t.FirstResponseDueAt < now)),
            })
            .ToListAsync(ct);
        var assigneeIds = loadRows.Select(r => r.UserId).ToList();
        var names = await _context.Users.IgnoreQueryFilters()
            .Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var load = loadRows
            .Select(r => new OpsAssigneeLoad(r.UserId, names.GetValueOrDefault(r.UserId), r.Open, r.Overdue))
            .OrderByDescending(l => l.OverdueCount)
            .ThenByDescending(l => l.OpenCount)
            .ToList();

        var recent = tickets.Where(t => t.CreatedAt >= recentFrom);
        var byCategory = await recent.GroupBy(t => t.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var recentCount = await recent.CountAsync(ct);
        var responded = await recent.CountAsync(t => t.FirstRespondedAt != null, ct);
        var respondedInTime = await recent.CountAsync(t => t.FirstRespondedAt != null && t.FirstRespondedAt <= t.FirstResponseDueAt, ct);
        var resolved = await recent.CountAsync(t => t.ResolvedAt != null, ct);
        var resolvedInTime = await recent.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt <= t.ResolveDueAt, ct);
        var ratingCount = await recent.CountAsync(t => t.Rating != null, ct);
        var averageRating = ratingCount == 0 ? (double?)null : await recent.Where(t => t.Rating != null).AverageAsync(t => (double)t.Rating!.Value, ct);
        var pendingApprovals = await _context.OpsApprovalRequests.CountAsync(a => a.Status == OpsApprovalStatus.Pending && a.ExpiresAt > now, ct);

        return new OpsDashboardData(
            byStatus.Select(x => new OpsCount(x.Key.ToString(), x.Count)).ToList(),
            byPriority.Select(x => new OpsCount(x.Key.ToString(), x.Count)).ToList(),
            unassigned,
            overdue,
            load,
            byCategory.Select(x => new OpsCount(x.Key.ToString(), x.Count)).ToList(),
            recentCount,
            responded,
            respondedInTime,
            resolved,
            resolvedInTime,
            ratingCount,
            averageRating,
            pendingApprovals);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
