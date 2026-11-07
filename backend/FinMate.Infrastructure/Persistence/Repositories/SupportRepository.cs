using FinMate.Application.Admin;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SupportRepository : ISupportRepository
{
    private readonly FinMateDbContext _context;

    public SupportRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public void AddTicket(SupportTicket ticket) => _context.SupportTickets.Add(ticket);

    public void AddMessage(SupportTicketMessage message) => _context.SupportTicketMessages.Add(message);

    public void AddAttachment(SupportTicketAttachment attachment) => _context.SupportTicketAttachments.Add(attachment);

    public Task<SupportTicket?> GetForRequesterAsync(Guid ticketId, Guid userId, CancellationToken ct = default)
        => _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.RequesterUserId == userId, ct);

    public Task<List<SupportTicket>> ListForRequesterAsync(Guid userId, CancellationToken ct = default)
        => _context.SupportTickets.AsNoTracking()
            .Where(t => t.RequesterUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

    public Task<int> CountOpenForRequesterAsync(Guid userId, CancellationToken ct = default)
        => _context.SupportTickets.CountAsync(t => t.RequesterUserId == userId && t.Status != SupportTicketStatus.Closed, ct);

    public Task<SupportTicket?> GetByIdAsync(Guid ticketId, CancellationToken ct = default)
        => _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);

    public async Task<OpsTicketRow?> GetRowAsync(Guid ticketId, CancellationToken ct = default)
        => await Rows(_context.SupportTickets.AsNoTracking().Where(t => t.Id == ticketId)).FirstOrDefaultAsync(ct);

    public async Task<(List<OpsTicketRow> Items, int Total)> QueueAsync(OpsTicketFilter filter, CancellationToken ct = default)
    {
        // Ca của chính người xem không hiện ra: họ không được xử lý, và ghi chú nội bộ về họ không được lộ.
        var query = _context.SupportTickets.AsNoTracking().Where(t => t.RequesterUserId != filter.ActorId);

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }
        else
        {
            query = query.Where(t => t.Status != SupportTicketStatus.Closed);
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (filter.Category is { } category)
        {
            query = query.Where(t => t.Category == category);
        }

        if (filter.UnassignedOnly)
        {
            query = query.Where(t => t.AssigneeUserId == null);
        }
        else if (filter.AssigneeUserId is { } assignee)
        {
            query = query.Where(t => t.AssigneeUserId == assignee);
        }

        if (filter.ParentTicketId is { } parent)
        {
            query = query.Where(t => t.ParentTicketId == parent);
        }

        if (filter.OverdueOnly)
        {
            var now = filter.Now;
            query = query.Where(t =>
                t.Status != SupportTicketStatus.Resolved
                && t.Status != SupportTicketStatus.Closed
                && (t.ResolveDueAt < now || (t.FirstRespondedAt == null && t.FirstResponseDueAt < now)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{AdminPaging.EscapeLike(filter.Search.Trim())}%";
            query = query.Where(t => EF.Functions.ILike(t.Subject, pattern, "\\")
                || _context.Users.IgnoreQueryFilters().Any(u => u.Id == t.RequesterUserId && EF.Functions.ILike(u.DisplayName, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await Rows(query.OrderBy(t => t.ResolveDueAt).ThenBy(t => t.Id).Skip(filter.Offset).Take(filter.Limit)).ToListAsync(ct);
        return (items, total);
    }

    // IgnoreQueryFilters: khách đã yêu cầu xóa tài khoản vẫn còn ca tới lúc xóa cứng.
    private IQueryable<OpsTicketRow> Rows(IQueryable<SupportTicket> tickets)
        => from t in tickets
           join r in _context.Users.IgnoreQueryFilters() on t.RequesterUserId equals r.Id
           join a in _context.Users.IgnoreQueryFilters() on t.AssigneeUserId equals (Guid?)a.Id into assignees
           from a in assignees.DefaultIfEmpty()
           select new OpsTicketRow(t, r.DisplayName, a == null ? null : a.DisplayName);

    public Task<bool> IsHandlingRequesterAsync(Guid actorId, Guid requesterUserId, CancellationToken ct = default)
        => _context.SupportTickets.AnyAsync(t => t.AssigneeUserId == actorId && t.RequesterUserId == requesterUserId && t.Status != SupportTicketStatus.Closed, ct);

    public Task<List<SupportTicket>> GetManyAsync(IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default)
        => _context.SupportTickets.Where(t => ticketIds.Contains(t.Id)).ToListAsync(ct);

    public Task<List<SupportTicket>> ListOpenChildrenAsync(Guid parentTicketId, CancellationToken ct = default)
        => _context.SupportTickets.Where(t => t.ParentTicketId == parentTicketId && t.Status != SupportTicketStatus.Closed).ToListAsync(ct);

    public Task<bool> HasChildrenAsync(Guid ticketId, CancellationToken ct = default)
        => _context.SupportTickets.AnyAsync(t => t.ParentTicketId == ticketId, ct);

    public async Task<List<SupportMessageRow>> ListMessagesAsync(Guid ticketId, bool includeInternal, CancellationToken ct = default)
    {
        var messages = _context.SupportTicketMessages.AsNoTracking().Where(m => m.TicketId == ticketId);
        if (!includeInternal)
        {
            messages = messages.Where(m => m.Kind != SupportMessageKind.InternalNote);
        }

        return await (from m in messages
                      join u in _context.Users.IgnoreQueryFilters() on m.AuthorUserId equals (Guid?)u.Id into authors
                      from u in authors.DefaultIfEmpty()
                      orderby m.CreatedAt, m.Id
                      select new SupportMessageRow(m, u == null ? null : u.DisplayName))
            .ToListAsync(ct);
    }

    public Task<SupportTicketMessage?> GetMessageAsync(Guid ticketId, Guid messageId, CancellationToken ct = default)
        => _context.SupportTicketMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId && m.TicketId == ticketId, ct);

    public Task<List<SupportAttachmentInfo>> ListAttachmentsAsync(Guid ticketId, CancellationToken ct = default)
        => _context.SupportTicketAttachments.AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new SupportAttachmentInfo(a.Id, a.MessageId, a.ContentType, a.SizeBytes, a.CreatedAt))
            .ToListAsync(ct);

    public Task<int> CountAttachmentsAsync(Guid messageId, CancellationToken ct = default)
        => _context.SupportTicketAttachments.CountAsync(a => a.MessageId == messageId, ct);

    public Task<SupportTicketAttachment?> GetAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default)
        => _context.SupportTicketAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId, ct);

    public void AddGrant(SupportAccessGrant grant) => _context.SupportAccessGrants.Add(grant);

    public void AddAccessLog(SupportDataAccessLog log) => _context.SupportDataAccessLogs.Add(log);

    public Task<SupportAccessGrant?> GetGrantAsync(Guid ticketId, Guid grantId, CancellationToken ct = default)
        => _context.SupportAccessGrants.FirstOrDefaultAsync(g => g.Id == grantId && g.TicketId == ticketId, ct);

    public Task<List<SupportAccessGrant>> ListGrantsAsync(Guid ticketId, CancellationToken ct = default)
        => _context.SupportAccessGrants.Where(g => g.TicketId == ticketId).OrderByDescending(g => g.CreatedAt).ToListAsync(ct);

    public Task EndGrantsAsync(Guid ticketId, DateTimeOffset now, CancellationToken ct = default)
        => _context.SupportAccessGrants
            .Where(g => g.TicketId == ticketId && (g.Status == SupportAccessStatus.Pending || g.Status == SupportAccessStatus.Approved))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, SupportAccessStatus.Revoked).SetProperty(g => g.RevokedAt, now), ct);

    public Task EndGrantsForRequesterAsync(Guid requesterUserId, DateTimeOffset now, CancellationToken ct = default)
        => _context.SupportAccessGrants
            .Where(g => (g.Status == SupportAccessStatus.Pending || g.Status == SupportAccessStatus.Approved)
                && _context.SupportTickets.Any(t => t.Id == g.TicketId && t.RequesterUserId == requesterUserId))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, SupportAccessStatus.Revoked).SetProperty(g => g.RevokedAt, now), ct);

    public Task<List<SupportDataAccessLog>> ListAccessLogsAsync(Guid customerUserId, CancellationToken ct = default)
        => _context.SupportDataAccessLogs.AsNoTracking()
            .Where(l => l.CustomerUserId == customerUserId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

    public async Task CloseTicketAsync(SupportTicket ticket, DateTimeOffset now, DateTimeOffset attachmentsDeleteAfter, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        ticket.Status = SupportTicketStatus.Closed;
        ticket.ClosedAt = now;
        ticket.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);

        await _context.SupportTicketAttachments
            .Where(a => a.TicketId == ticket.Id && a.DeleteAfter == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeleteAfter, attachmentsDeleteAfter), ct);
        await EndGrantsAsync(ticket.Id, now, ct);

        await transaction.CommitAsync(ct);
    }

    public Task<List<SupportTicket>> ListResolvedBeforeAsync(DateTimeOffset before, int limit, CancellationToken ct = default)
        => _context.SupportTickets
            .Where(t => t.Status == SupportTicketStatus.Resolved && t.ResolvedAt < before)
            .OrderBy(t => t.ResolvedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> DeleteExpiredAttachmentsAsync(DateTimeOffset now, CancellationToken ct = default)
        => _context.SupportTicketAttachments.Where(a => a.DeleteAfter != null && a.DeleteAfter <= now).ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
