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

    /// <summary>
    /// Phase 97, luật người trả + thời điểm: với mỗi khoản định kỳ có người trả/nhận, mỗi kỳ đến hạn (từ <see cref="RecurringItemView.NextDueDate"/> lùi về đầu
    /// <paramref name="transactions"/>, rồi tiến từ kỳ sau nó qua <see cref="RecurringSchedule.DueDates"/> tới cuối khoảng) nhận đúng MỘT giao dịch cùng người, cùng chiều trong <see cref="RecurringSchedule.PaymentWindow"/> làm lần trả của kỳ đó,
    /// bất kể số tiền: ưu tiên giao dịch trong dung sai, không có thì giao dịch gần ngày đến hạn nhất (bằng nhau lấy giao dịch sớm hơn). Lương hay hóa đơn đã đổi
    /// giá vì vậy không còn rơi vào thu/chi linh hoạt ở các kỳ giá cũ, trong khi thưởng, dự án lẻ hay mua thêm của cùng người, ngoài cửa sổ hoặc cùng cửa sổ với
    /// lần trả thật, vẫn là linh hoạt.
    /// </summary>
    public static IReadOnlySet<Guid> PeriodPayments(
        IReadOnlyList<ForecastTransaction> transactions, IReadOnlyList<RecurringItemView> items, ForecastRules rules)
    {
        var ids = new HashSet<Guid>();
        if (transactions.Count == 0)
        {
            return ids;
        }

        var (first, last) = (transactions.Min(t => t.Date), transactions.Max(t => t.Date));
        foreach (var item in items.Where(i => i.CounterpartyKey is not null))
        {
            var candidates = transactions.Where(t => SameCounterparty(item, t)).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            // Lùi từ NextDueDate về đầu khoảng giao dịch.
            for (var due = item.NextDueDate; ; due = RecurringSchedule.Previous(due, item.Cadence, item.PreferredDay))
            {
                var window = RecurringSchedule.PaymentWindow(due, item.Cadence, rules.OverdueGraceDays);
                if (window.To < first)
                {
                    break;
                }

                ClaimPeriod(item, due, window, candidates, ids, rules);
            }

            // Tiến từ kỳ sau NextDueDate tới cuối khoảng giao dịch: sau một kỳ bị bỏ, tracker dừng ở kỳ chưa trả, nên lần trả của các kỳ sau
            // (ví dụ trả gộp hai kỳ, ngoài dung sai) nằm sau NextDueDate. Đi qua DueDates để khoản trả góp đã hết kỳ không nhận thêm giao dịch nào.
            foreach (var due in RecurringSchedule.DueDates(item).Skip(1))
            {
                var window = RecurringSchedule.PaymentWindow(due, item.Cadence, rules.OverdueGraceDays);
                if (window.From > last)
                {
                    break;
                }

                ClaimPeriod(item, due, window, candidates, ids, rules);
            }
        }

        return ids;
    }

    /// <summary>Nhận đúng một giao dịch chưa được nhận trong cửa sổ của kỳ <paramref name="due"/>: trong dung sai trước, rồi gần hạn nhất, rồi sớm nhất.</summary>
    private static void ClaimPeriod(
        RecurringItemView item, DateOnly due, (DateOnly From, DateOnly To) window, List<ForecastTransaction> candidates, HashSet<Guid> ids, ForecastRules rules)
    {
        var payment = candidates
            .Where(t => t.Date >= window.From && t.Date <= window.To && !ids.Contains(t.Id))
            .OrderBy(t => rules.AmountWithinTolerance(item.AmountCents, t.AmountCents) ? 0 : 1)
            .ThenBy(t => Math.Abs(t.Date.DayNumber - due.DayNumber))
            .ThenBy(t => t.TransactedAt)
            .FirstOrDefault();
        if (payment is not null)
        {
            ids.Add(payment.Id);
        }
    }

    private static bool SameDirection(RecurringDirection direction, TransactionType type)
        => (direction == RecurringDirection.Outflow && type == TransactionType.Debit)
           || (direction == RecurringDirection.Inflow && type == TransactionType.Credit);
}

/// <summary>
/// Phase 97: bộ lọc dùng chung cho thu và chi linh hoạt. Một giao dịch là lần trả của khoản định kỳ (nên không vào thói quen) khi nó khớp khoản đó trong dung sai
/// (<see cref="RecurringMatcher.MatchesAny"/>), hoặc là lần trả của một kỳ theo luật người trả + thời điểm (<see cref="RecurringMatcher.PeriodPayments"/>).
/// </summary>
public sealed class RecurringPaymentFilter
{
    private readonly IReadOnlyList<RecurringItemView> _items;
    private readonly ForecastRules _rules;
    private readonly IReadOnlySet<Guid> _periodPayments;

    private RecurringPaymentFilter(IReadOnlyList<RecurringItemView> items, ForecastRules rules, IReadOnlySet<Guid> periodPayments)
        => (_items, _rules, _periodPayments) = (items, rules, periodPayments);

    /// <param name="transactions">Mọi giao dịch sẽ được hỏi qua <see cref="IsPayment"/>; kỳ đến hạn được xét trong khoảng ngày của chúng.</param>
    public static RecurringPaymentFilter For(IReadOnlyList<ForecastTransaction> transactions, IReadOnlyList<RecurringItemView> items, ForecastRules rules)
        => new(items, rules, RecurringMatcher.PeriodPayments(transactions, items, rules));

    public bool IsPayment(ForecastTransaction transaction)
        => _periodPayments.Contains(transaction.Id) || RecurringMatcher.MatchesAny(transaction, _items, _rules);
}
