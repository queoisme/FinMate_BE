using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.Transactions;
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

    public async Task<IReadOnlyList<ForecastTransaction>> GetBoundaryTransfersAsync(
        Guid userId, IReadOnlySet<Guid> countedAccountIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);
        var counted = countedAccountIds.ToList();
        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType == TransactionType.Transfer
                && t.CounterAccountId != null
                && counted.Contains(t.FinancialAccountId) != counted.Contains(t.CounterAccountId!.Value)
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .Select(t => new
            {
                t.Id,
                t.FinancialAccountId,
                CounterAccountId = t.CounterAccountId!.Value,
                t.AmountCents,
                t.TransactedAt,
                t.MerchantName,
                SourceName = t.FinancialAccount!.AccountName,
                CounterName = t.CounterAccount!.AccountName,
            })
            .ToListAsync(ct);

        var walletToWallet = rows.Select(r =>
        {
            var outgoing = counted.Contains(r.FinancialAccountId);
            var name = r.MerchantName ?? (outgoing ? r.CounterName : r.SourceName);
            return new ForecastTransaction(
                r.Id, outgoing ? r.FinancialAccountId : r.CounterAccountId, null, r.AmountCents, outgoing ? TransactionType.Debit : TransactionType.Credit,
                r.TransactedAt, VietnamTime.DateOf(r.TransactedAt), name, CounterpartyKey.From(name), false);
        });

        return walletToWallet.Concat(await GetGroupFundBoundaryAsync(userId, counted, start, end, ct)).ToList();
    }

    /// <summary>
    /// Phase 85 (BR-FUND-08): giao dịch gắn quỹ nhóm có đầu kia là QUỸ, không phải ví, nên luôn vượt ranh giới khi ví của nó được tính. Hướng lấy qua
    /// <see cref="TransferLegs"/> (không đọc cột hướng ở đây); tên là tên quỹ, để bộ khoản định kỳ nhận ra "góp quỹ hằng tháng".
    /// </summary>
    private async Task<IEnumerable<ForecastTransaction>> GetGroupFundBoundaryAsync(
        Guid userId, List<Guid> counted, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType == TransactionType.Transfer
                && t.CounterAccountId == null
                && t.GroupFundEntryId != null
                && counted.Contains(t.FinancialAccountId)
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .Select(t => new { Transaction = t, FundName = t.GroupFundEntry!.Fund!.Name })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var leg = TransferLegs.Of(r.Transaction).Single();
            var name = r.FundName;
            return new ForecastTransaction(
                r.Transaction.Id, leg.AccountId, null, r.Transaction.AmountCents, leg.DeltaCents < 0 ? TransactionType.Debit : TransactionType.Credit,
                r.Transaction.TransactedAt, VietnamTime.DateOf(r.Transaction.TransactedAt), name, CounterpartyKey.From(name), false);
        });
    }

    public async Task<IReadOnlyList<ForecastTransaction>> GetRawTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);
        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(LoanExclusion.NotLoan)   // cho vay/mượn không phải thu/chi thật
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType != TransactionType.Transfer
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
                t.DuplicateOfTransactionId,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new ForecastTransaction(
                r.Id, r.FinancialAccountId, r.CategoryId, r.AmountCents, r.TransactionType, r.TransactedAt,
                VietnamTime.DateOf(r.TransactedAt), r.MerchantName, CounterpartyKey.From(r.MerchantName), r.ExcludeFromForecast,
                r.Relation, r.RelatedTransactionId, r.IsReversed, r.DuplicateOfTransactionId))
            .ToList();
    }
}
