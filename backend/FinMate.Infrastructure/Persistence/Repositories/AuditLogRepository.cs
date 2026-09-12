using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly FinMateDbContext _context;

    public AuditLogRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<AuditLogListResult> GetPageAsync(
        AuditLogListFilter filter, CancellationToken ct = default)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(filter.EventType))
        {
            query = query.Where(a => a.EventType == filter.EventType);
        }

        if (filter.FromDate is { } from)
        {
            query = query.Where(a => a.CreatedAt >= from);
        }

        if (filter.ToDate is { } to)
        {
            query = query.Where(a => a.CreatedAt <= to);
        }

        if (KeysetCursor.TryDecode(filter.Cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(a =>
                a.CreatedAt < cursorAt || (a.CreatedAt == cursorAt && a.Id.CompareTo(cursorId) < 0));
        }

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(filter.Limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > filter.Limit)
        {
            var last = items[filter.Limit - 1];
            nextCursor = KeysetCursor.Encode(last.CreatedAt, last.Id);
            items = items.Take(filter.Limit).ToList();
        }

        return new AuditLogListResult(items, nextCursor);
    }
}
