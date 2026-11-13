using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ForecastDataRepository : IForecastDataRepository
{
    private readonly FinMateDbContext _context;

    public ForecastDataRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ForecastTransaction>> GetTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => RefundNetting.ForHabits(await GetRawTransactionsAsync(userId, from, to, ct));

    public async Task<IReadOnlyList<ForecastTransaction>> GetRawTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);
        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType != TransactionType.Transfer
                && (t.Category == null || t.Category.Slug != Category.LoanSlug)   // cho vay/mượn không phải thu/chi thật
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .Select(t => new
            {
                t.Id,
                t.FinancialAccountId,
                t.CategoryId,
                t.AmountCents,
                t.TransactionType,
                t.TransactedAt,
                t.MerchantName,
                t.ExcludeFromForecast,
                t.Relation,
                t.RelatedTransactionId,
                t.IsReversed,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new ForecastTransaction(
                r.Id, r.FinancialAccountId, r.CategoryId, r.AmountCents, r.TransactionType, r.TransactedAt,
                VietnamTime.DateOf(r.TransactedAt), r.MerchantName, CounterpartyKey.From(r.MerchantName), r.ExcludeFromForecast,
                r.Relation, r.RelatedTransactionId, r.IsReversed))
            .ToList();
    }
}
