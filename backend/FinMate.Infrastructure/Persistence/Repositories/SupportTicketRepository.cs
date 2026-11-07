using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SupportTicketRepository(FinMateDbContext context) : ISupportTicketRepository
{
    public Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<SupportTicket?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default)
        => context.SupportTickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

    public async Task<(int Total, List<SupportTicket> Items)> ListForUserAsync(Guid userId, SupportTicketStatus? status, int limit, int offset, CancellationToken ct = default)
    {
        var query = context.SupportTickets.AsNoTracking().Where(t => t.UserId == userId && (status == null || t.Status == status));
        return (await query.CountAsync(ct), await query.OrderByDescending(t => t.UpdatedAt).Skip(offset).Take(limit).ToListAsync(ct));
    }

    public async Task<(int Total, List<SupportTicket> Items)> ListAllAsync(SupportTicketStatus? status, string? search, int limit, int offset, CancellationToken ct = default)
    {
        var query = context.SupportTickets.AsNoTracking().Where(t => status == null || t.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(t => EF.Functions.ILike(t.Subject, pattern));
        }

        return (await query.CountAsync(ct), await query.OrderByDescending(t => t.UpdatedAt).Skip(offset).Take(limit).ToListAsync(ct));
    }

    public Task<int> CountOpenForUserAsync(Guid userId, CancellationToken ct = default)
        => context.SupportTickets.CountAsync(t => t.UserId == userId && t.Status != SupportTicketStatus.Resolved && t.Status != SupportTicketStatus.Rejected, ct);

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        context.SupportTickets.Add(ticket);
        await context.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(SupportTicket ticket, CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
