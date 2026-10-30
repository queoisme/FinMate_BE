using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class QrTransferRepository : IQrTransferRepository
{
    private readonly FinMateDbContext _context;

    public QrTransferRepository(FinMateDbContext context)
    {
        _context = context;
    }

    // ------------------------------------------------------------------ ngân hàng
    public async Task<IReadOnlyList<QrBankView>> GetActiveBanksAsync(CancellationToken ct = default)
    {
        var banks = await _context.VietQrBanks.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        var activeProviders = (await _context.ProviderConfigs.AsNoTracking().Where(p => p.IsActive).Select(p => p.ProviderKey).ToListAsync(ct)).ToHashSet();
        return banks.Select(b => new QrBankView(b, b.ProviderKey is not null && activeProviders.Contains(b.ProviderKey))).ToList();
    }

    public Task<string?> GetBankBinByPackageAsync(string packageName, CancellationToken ct = default)
        => (from p in _context.ProviderConfigs.AsNoTracking()
            where p.PackageName == packageName
            join b in _context.VietQrBanks.AsNoTracking() on p.ProviderKey equals b.ProviderKey
            where b.IsActive
            select b.Bin).FirstOrDefaultAsync(ct);

    // ------------------------------------------------------------------ ý định chuyển tiền
    public async Task AddIntentAsync(QrTransferIntent intent, CancellationToken ct = default)
    {
        _context.QrTransferIntents.Add(intent);
        await _context.SaveChangesAsync(ct);
    }

    public Task<QrTransferIntent?> GetIntentAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.QrTransferIntents.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);

    public async Task<IReadOnlyList<QrTransferIntent>> ListIntentsAsync(Guid userId, QrTransferStatus? status, int limit, CancellationToken ct = default)
    {
        var query = _context.QrTransferIntents.AsNoTracking().Where(i => i.UserId == userId);
        if (status is { } wanted)
        {
            query = query.Where(i => i.Status == wanted);
        }

        return await query.OrderByDescending(i => i.ScannedAt).Take(limit).ToListAsync(ct);
    }

    public Task<QrTransferIntent?> FindPendingAsync(
        Guid userId, string senderBankBin, long amountCents, DateTimeOffset notBefore, DateTimeOffset notAfter, CancellationToken ct = default)
        => _context.QrTransferIntents
            .Where(i => i.UserId == userId
                        && i.Status == QrTransferStatus.Pending
                        && i.SenderBankBin == senderBankBin
                        && i.AmountCents == amountCents
                        && i.ScannedAt >= notBefore
                        && i.ScannedAt <= notAfter)
            .OrderByDescending(i => i.ScannedAt)
            .FirstOrDefaultAsync(ct);

    public Task<QrTransferIntent?> GetIntentByTransactionAsync(Guid transactionId, Guid userId, CancellationToken ct = default)
        => _context.QrTransferIntents.FirstOrDefaultAsync(i => i.MatchedTransactionId == transactionId && i.UserId == userId, ct);

    public async Task UpdateIntentAsync(QrTransferIntent intent, CancellationToken ct = default)
    {
        _context.QrTransferIntents.Update(intent);
        await _context.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ người nhận → danh mục
    public Task<CounterpartyCategoryOverride?> GetOverrideAsync(Guid userId, string bankBin, string accountNumber, CancellationToken ct = default)
        => _context.CounterpartyCategoryOverrides.FirstOrDefaultAsync(
            o => o.UserId == userId && o.BankBin == bankBin && o.AccountNumber == accountNumber, ct);

    public Task<CounterpartyCategoryOverride?> GetOverrideByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.CounterpartyCategoryOverrides.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);

    public async Task<IReadOnlyList<CounterpartyCategoryOverride>> ListOverridesAsync(Guid userId, CancellationToken ct = default)
        => await _context.CounterpartyCategoryOverrides.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.UpdatedAt)
            .ToListAsync(ct);

    public async Task AddOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default)
    {
        _context.CounterpartyCategoryOverrides.Add(mapping);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default)
    {
        _context.CounterpartyCategoryOverrides.Update(mapping);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default)
    {
        _context.CounterpartyCategoryOverrides.Remove(mapping);
        await _context.SaveChangesAsync(ct);
    }
}
