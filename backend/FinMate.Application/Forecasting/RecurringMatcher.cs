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

    /// <summary>
    /// Giao dịch đã trả cho lần đến hạn <paramref name="due"/>; null nếu chưa thấy. Ưu tiên giao dịch đúng số tiền (trong dung sai); không có
    /// thì nhận giao dịch cùng người nhận, cùng chiều trong cửa sổ dù số tiền khác — đó là lần trả với GIÁ MỚI (sơ đồ 5a: "Số tiền mới khác các lần trước?"),
    /// không phải chưa trả. Coi nó là chưa trả sẽ trừ hóa đơn hai lần.
    /// </summary>
    public static ForecastTransaction? FindPayment(
        RecurringItemView item, DateOnly due, IEnumerable<ForecastTransaction> transactions, ForecastRules rules)
    {
        var (from, to) = RecurringSchedule.PaymentWindow(due, item.Cadence, rules.OverdueGraceDays);
        var candidates = transactions
            .Where(t => t.Date >= from && t.Date <= to && SameCounterparty(item, t))
            .OrderBy(t => t.TransactedAt)
            .ToList();
        return candidates.FirstOrDefault(t => rules.AmountWithinTolerance(item.AmountCents, t.AmountCents)) ?? candidates.FirstOrDefault();
    }

    private static bool SameCounterparty(RecurringItemView item, ForecastTransaction transaction)
        => item.CounterpartyKey is not null
           && item.CounterpartyKey == transaction.CounterpartyKey
           && SameDirection(item.Direction, transaction.Type);

    /// <summary>Giao dịch này có khớp khoản định kỳ đang theo dõi nào không — dùng để loại nó khỏi thói quen chi tiêu (tránh trừ hai lần).</summary>
    public static bool MatchesAny(ForecastTransaction transaction, IReadOnlyList<RecurringItemView> items, ForecastRules rules)
        => items.Any(i => Matches(i, transaction, rules));

    private static bool SameDirection(RecurringDirection direction, TransactionType type)
        => (direction == RecurringDirection.Outflow && type == TransactionType.Debit)
           || (direction == RecurringDirection.Inflow && type == TransactionType.Credit);
}
