using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ReceiptScanRepository : IReceiptScanRepository
{
    private readonly FinMateDbContext _context;

    public ReceiptScanRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ReceiptScan scan, CancellationToken ct = default)
    {
        _context.ReceiptScans.Add(scan);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> LinkToTransactionAsync(Guid scanId, Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var linked = await _context.ReceiptScans
            .Where(s => s.Id == scanId && s.UserId == userId && s.TransactionId == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.TransactionId, transactionId), ct);
        return linked == 1;
    }

    public Task<ReceiptScan?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.ReceiptScans.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
}
