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
}
