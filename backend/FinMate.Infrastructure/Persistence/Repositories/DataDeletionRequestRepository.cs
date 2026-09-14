using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class DataDeletionRequestRepository : IDataDeletionRequestRepository
{
    private readonly FinMateDbContext _context;

    public DataDeletionRequestRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(DataDeletionRequest request, CancellationToken ct = default)
    {
        _context.DataDeletionRequests.Add(request);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<DataDeletionRequest>> GetDueAsync(DateTimeOffset asOf, CancellationToken ct = default)
        => _context.DataDeletionRequests
            .Where(d => d.Status == DataDeletionStatus.Pending && d.ScheduledHardDeleteAt <= asOf)
            .ToListAsync(ct);

    public async Task MarkProcessedAsync(DataDeletionRequest request, CancellationToken ct = default)
    {
        request.Status = DataDeletionStatus.Processed;
        request.ProcessedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public Task<DataDeletionRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.DataDeletionRequests.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<DataDeletionRequestListResult> GetPageAsync(
        DataDeletionRequestListFilter filter, CancellationToken ct = default)
    {
        // IgnoreQueryFilters trên Users: mọi dòng ở đây trỏ tới một tài khoản đã soft delete,
        // nên join thường sẽ lọc sạch đúng những dòng cần hiện.
        var query =
            from request in _context.DataDeletionRequests.AsNoTracking()
            join user in _context.Users.IgnoreQueryFilters().AsNoTracking()
                on request.UserId equals user.Id
            select new { Request = request, user.Email, user.DisplayName };

        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Request.Status == status);
        }

        if (KeysetCursor.TryDecode(filter.Cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(r =>
                r.Request.RequestedAt < cursorAt
                || (r.Request.RequestedAt == cursorAt && r.Request.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.Request.RequestedAt)
            .ThenByDescending(r => r.Request.Id)
            .Take(filter.Limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > filter.Limit)
        {
            var last = rows[filter.Limit - 1];
            nextCursor = KeysetCursor.Encode(last.Request.RequestedAt, last.Request.Id);
            rows = rows.Take(filter.Limit).ToList();
        }

        return new DataDeletionRequestListResult(
            rows.Select(r => new DataDeletionRequestRow(r.Request, r.Email, r.DisplayName)).ToList(),
            nextCursor);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
