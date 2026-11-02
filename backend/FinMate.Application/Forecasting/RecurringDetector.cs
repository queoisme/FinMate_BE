using FinMate.Application.Reports;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Một khoản định kỳ mới tìm thấy trong lịch sử (sơ đồ 5a: "Lặp lại đều đặn với số tiền ổn định?").</summary>
public sealed record DetectedRecurring(
    RecurringDirection Direction,
    string Name,
    string CounterpartyKey,
    Guid? CategoryId,
    Guid AccountId,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    int Occurrences,
    RecurringStatus Status,
    DateTimeOffset LastSeenAt,
    long LastSeenAmountCents);

/// <summary>
/// Tìm khoản thu/chi lặp lại đều đặn: cùng người nhận, cùng chiều, số tiền lệch trong dung sai, cách nhau đúng chu kỳ tuần hoặc tháng.
/// Đủ <see cref="ForecastRules.AutoActivateOccurrences"/> lần thì tự ghi nhận, đủ <see cref="ForecastRules.SuggestMinOccurrences"/> thì chỉ đề xuất.
/// Chu kỳ năm không tự phát hiện (cần hơn một năm dữ liệu): user thêm tay.
/// </summary>
public static class RecurringDetector
{
    /// <param name="knownKeys">Cặp (chiều, khóa người nhận) đã có khoản định kỳ ở BẤT KỲ trạng thái nào — kể cả đã từ chối/hủy, để không đề xuất lại.</param>
    public static IReadOnlyList<DetectedRecurring> Detect(
        IEnumerable<ForecastTransaction> history,
        IReadOnlySet<(RecurringDirection Direction, string Key)> knownKeys,
        DateOnly today,
        ForecastRules rules)
    {
        var results = new List<DetectedRecurring>();
        var groups = history
            .Where(t => !t.ExcludedFromForecast && t.CounterpartyKey is not null && t.Type is TransactionType.Debit or TransactionType.Credit)
            .GroupBy(t => (Direction: t.Type == TransactionType.Credit ? RecurringDirection.Inflow : RecurringDirection.Outflow, Key: t.CounterpartyKey!));

        foreach (var group in groups)
        {
            if (knownKeys.Contains(group.Key))
            {
                continue;
            }

            var candidate = Evaluate(group.Key.Direction, group.Key.Key, group.OrderBy(t => t.TransactedAt).ToList(), today, rules);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static DetectedRecurring? Evaluate(
        RecurringDirection direction, string key, IReadOnlyList<ForecastTransaction> ordered, DateOnly today, ForecastRules rules)
    {
        if (ordered.Count < rules.SuggestMinOccurrences)
        {
            return null;
        }

        var median = (long)Math.Round(Statistics.Median(ordered.Select(t => (double)t.AmountCents).ToArray()));
        if (median <= 0 || !ordered.All(t => rules.AmountWithinTolerance(median, t.AmountCents)))
        {
            return null;
        }

        var cadence = CadenceOf(ordered, rules);
        if (cadence is null)
        {
            return null;
        }

        var last = ordered[^1];
        var nextDue = RecurringSchedule.Next(last.Date, cadence.Value, last.Date.Day);

        // Lần kế tiếp đã quá hạn lâu mà chưa thấy: nhiều khả năng khoản này đã dừng, đề xuất nó chỉ gây nhiễu.
        if (nextDue < today.AddDays(-rules.OverdueGraceDays))
        {
            return null;
        }

        var status = ordered.Count >= rules.AutoActivateOccurrences ? RecurringStatus.Active : RecurringStatus.Suggested;
        return new DetectedRecurring(
            direction, DisplayName(last, key), key, last.CategoryId, last.AccountId, median, cadence.Value, nextDue,
            ordered.Count, status, last.TransactedAt, last.AmountCents);
    }

    private static RecurringCadence? CadenceOf(IReadOnlyList<ForecastTransaction> ordered, ForecastRules rules)
    {
        var gaps = ordered.Zip(ordered.Skip(1), (a, b) => b.Date.DayNumber - a.Date.DayNumber).ToList();
        if (gaps.All(d => d >= rules.MonthlyMinIntervalDays && d <= rules.MonthlyMaxIntervalDays))
        {
            return RecurringCadence.Monthly;
        }

        if (gaps.All(d => d >= rules.WeeklyMinIntervalDays && d <= rules.WeeklyMaxIntervalDays))
        {
            return RecurringCadence.Weekly;
        }

        return null;
    }

    private static string DisplayName(ForecastTransaction last, string key)
    {
        var name = string.IsNullOrWhiteSpace(last.MerchantName) ? key : last.MerchantName.Trim();
        return name.Length <= RecurringItemLimits.NameMaxLength ? name : name[..RecurringItemLimits.NameMaxLength];
    }
}

public static class RecurringItemLimits
{
    public const int NameMaxLength = 100;
}
