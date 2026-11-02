using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Giao dịch nào thuộc khoản định kỳ nào. Dùng chung cho engine (lần này đã trả chưa) và job (đẩy ngày đến hạn).</summary>
public static class RecurringMatcher
{
    public static bool Matches(RecurringItemView item, ForecastTransaction transaction, ForecastRules rules)
        => item.CounterpartyKey is not null
           && item.CounterpartyKey == transaction.CounterpartyKey
           && SameDirection(item.Direction, transaction.Type)
           && rules.AmountWithinTolerance(item.AmountCents, transaction.AmountCents);

    /// <summary>Giao dịch đã trả cho lần đến hạn <paramref name="due"/>, sớm nhất trước; null nếu chưa thấy.</summary>
    public static ForecastTransaction? FindPayment(
        RecurringItemView item, DateOnly due, IEnumerable<ForecastTransaction> transactions, ForecastRules rules)
    {
        var (from, to) = RecurringSchedule.PaymentWindow(due, item.Cadence, rules.OverdueGraceDays);
        return transactions
            .Where(t => t.Date >= from && t.Date <= to && Matches(item, t, rules))
            .OrderBy(t => t.TransactedAt)
            .FirstOrDefault();
    }

    /// <summary>Giao dịch này có khớp khoản định kỳ đang theo dõi nào không — dùng để loại nó khỏi thói quen chi tiêu (tránh trừ hai lần).</summary>
    public static bool MatchesAny(ForecastTransaction transaction, IReadOnlyList<RecurringItemView> items, ForecastRules rules)
        => items.Any(i => Matches(i, transaction, rules));

    private static bool SameDirection(RecurringDirection direction, TransactionType type)
        => (direction == RecurringDirection.Outflow && type == TransactionType.Debit)
           || (direction == RecurringDirection.Inflow && type == TransactionType.Credit);
}
