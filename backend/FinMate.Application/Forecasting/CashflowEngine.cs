using FinMate.Application.Admin.SystemConfig;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public static class CashflowEventKinds
{
    public const string RecurringInflow = "recurring_inflow";
    public const string RecurringOutflow = "recurring_outflow";
    public const string Scenario = "scenario";
    public const string Flexible = "flexible";

    /// <summary>Phase 90 (BR-DEBT-15): trả khoản đi vay đến hạn, ở cả ba đường.</summary>
    public const string LoanOutflow = "loan_outflow";

    /// <summary>Phase 90 (BR-DEBT-16): khoản cho vay đến hạn, CHỈ ở đường lạc quan.</summary>
    public const string LoanInflow = "loan_inflow";
}

/// <summary>Nguồn của một nguyên nhân / sự kiện mà người dùng tự nhập (Phase 90: khoản vay).</summary>
public static class CashflowSources
{
    public const string UserEntered = "user_entered";
}

/// <summary>Sự kiện tính vào đường nào. <see cref="OptimisticOnly"/>: chỉ đường lạc quan (khoản cho vay: người ta có thể không trả, BR-DEBT-16).</summary>
public enum CashflowBand
{
    All,
    OptimisticOnly,
}

/// <summary>Một khoản tiền vào/ra đã biết ngày. <paramref name="AmountCents"/> có dấu: dương là tiền vào, âm là tiền ra.</summary>
public sealed record CashflowEvent(
    DateOnly Date, long AmountCents, string Label, string Kind, Guid? RecurringItemId, CashflowBand Band = CashflowBand.All, Guid? LoanId = null);

public sealed record CashflowInput(
    DateOnly Today,
    DateOnly End,
    long StartBalanceCents,
    SpendingBaseline Baseline,
    long SpentTodayFlexibleCents,
    IReadOnlyList<CashflowEvent> Events,
    IReadOnlyList<ForecastHoliday> Holidays);

/// <param name="OptimisticOnly">Phase 90: chỉ tính vào đường lạc quan (<c>HighBalanceCents</c>), không vào số dư dự kiến hay thận trọng.</param>
public sealed record CashflowEventDto(string Kind, string Label, long AmountCents, Guid? RecurringItemId, bool OptimisticOnly = false, Guid? LoanId = null);

public sealed record CashflowDayDto(
    DateOnly Date,
    long ExpectedBalanceCents,
    long LowBalanceCents,
    long HighBalanceCents,
    long FlexibleSpendCents,
    IReadOnlyList<CashflowEventDto> Events);

/// <param name="Source">Phase 90: <c>user_entered</c> cho khoản vay người dùng tự ghi; null với các nguyên nhân khác.</param>
public sealed record CashflowCauseDto(string Kind, string Label, long AmountCents, string? Source = null);

/// <param name="LowBalanceCents">Đường thận trọng: chi linh hoạt theo phân vị 75.</param>
/// <param name="HighBalanceCents">Đường lạc quan: chi linh hoạt theo phân vị 25.</param>
public sealed record CashflowForecastDto(
    DateOnly From,
    DateOnly To,
    long StartBalanceCents,
    long EndBalanceCents,
    long EndLowBalanceCents,
    long EndHighBalanceCents,
    DateOnly LowestDay,
    long LowestBalanceCents,
    string Confidence,
    string Basis,
    IReadOnlyList<CashflowDayDto> Days,
    IReadOnlyList<CashflowCauseDto> MainCauses)
{
    /// <summary>Phase 84 (BR-LH-08): độ phủ dữ liệu; chỉ API gắn vào, engine luôn để null.</summary>
    public ListenerHealth.DataCoverageSummaryDto? DataCoverage { get; init; }

    /// <summary>Phase 90 (D13): khoản vay không có hạn nên không vào dự báo; null khi không có.</summary>
    public UndatedLoansDto? UndatedLoans { get; init; }

    /// <summary>Phase 90 (S5): giao dịch <c>loan</c> gần đây chưa gắn khoản vay nào; null khi không có. Không đổi con số dự báo.</summary>
    public UnlinkedLoanTransactionsDto? UnlinkedLoanTransactions { get; init; }
}

/// <summary>
/// Đường số dư dự kiến theo ngày (sơ đồ "Dự báo dòng tiền / số dư cuối kỳ"). Hàm thuần: cùng input luôn ra cùng kết quả, nên thử kịch bản
/// chỉ là gọi lại với thêm vài khoản giả định — không ghi gì vào dữ liệu thật.
/// </summary>
public static class CashflowEngine
{
    private const int NeutralMultiplierPercent = 100;
    private const int MaxCauses = 3;

    public static CashflowForecastDto Project(CashflowInput input)
    {
        var expected = input.StartBalanceCents;
        var low = input.StartBalanceCents;
        var high = input.StartBalanceCents;
        var eventsByDay = input.Events.ToLookup(e => e.Date);
        var days = new List<CashflowDayDto>();
        long flexibleTotal = 0;

        for (var date = input.Today; date <= input.End; date = date.AddDays(1))
        {
            var multiplier = MultiplierOn(date, input.Holidays);
            var (spendExpected, spendLow, spendHigh) = FlexibleSpend(input, date, multiplier);
            var events = eventsByDay[date].ToList();
            var eventsSum = events.Where(e => e.Band == CashflowBand.All).Sum(e => e.AmountCents);
            var optimisticSum = events.Sum(e => e.AmountCents);

            expected += eventsSum - spendExpected;
            low += eventsSum - spendHigh;
            high += optimisticSum - spendLow;
            flexibleTotal += spendExpected;

            days.Add(new CashflowDayDto(
                date, expected, low, high, spendExpected,
                events.Select(e => new CashflowEventDto(e.Kind, e.Label, e.AmountCents, e.RecurringItemId, e.Band == CashflowBand.OptimisticOnly, e.LoanId)).ToList()));
        }

        var lowest = days.MinBy(d => d.ExpectedBalanceCents)!;
        var last = days[^1];
        return new CashflowForecastDto(
            input.Today, input.End, input.StartBalanceCents,
            last.ExpectedBalanceCents, last.LowBalanceCents, last.HighBalanceCents,
            lowest.Date, lowest.ExpectedBalanceCents,
            input.Baseline.Confidence == ForecastConfidence.Low ? "low" : "normal",
            input.Baseline.Basis,
            days,
            MainCauses(input.Events, flexibleTotal));
    }

    /// <summary>
    /// Đặt các lần đến hạn của khoản định kỳ lên trục ngày. Lần hiện tại đã thấy thanh toán thì bỏ (tiền đã ra khỏi số dư). Lần đã quá hạn
    /// mà chưa thấy: khoản CHI còn trong ân hạn thì coi như sẽ trả hôm nay (thận trọng), quá ân hạn thì không tính nữa; khoản THU quá hạn
    /// thì không tính (sơ đồ trang 5: "Cập nhật dự báo theo kịch bản chưa nhận thu nhập") — trông vào tiền chưa về là lạc quan.
    /// </summary>
    public static IReadOnlyList<CashflowEvent> RecurringEvents(
        IReadOnlyList<RecurringItemView> items,
        IReadOnlyList<ForecastTransaction> recentTransactions,
        DateOnly today,
        DateOnly end,
        ForecastRules rules)
    {
        var events = new List<CashflowEvent>();
        foreach (var item in items)
        {
            var kind = item.Direction == RecurringDirection.Inflow ? CashflowEventKinds.RecurringInflow : CashflowEventKinds.RecurringOutflow;
            var signed = item.Direction == RecurringDirection.Inflow ? item.AmountCents : -item.AmountCents;
            foreach (var due in DueDates(item, today, end, rules))
            {
                if (RecurringMatcher.FindPayment(item, due, recentTransactions, rules) is not null
                    || (due < today && item.Direction == RecurringDirection.Inflow))
                {
                    continue;
                }

                events.Add(new CashflowEvent(OverdueDate(item, due, today), signed, item.Name, kind, item.Id));
            }
        }

        return events;
    }

    /// <summary>
    /// Các lần đến hạn cần xét, đã giới hạn theo kỳ cuối (<see cref="RecurringSchedule.DueDates"/>). Lần quá ân hạn bị bỏ (vẫn tính là một kỳ
    /// đã qua), trừ khoản CHI trả góp theo tổng số kỳ (Phase 92, S1): kỳ quá hạn chưa trả vẫn phải trả, không mất đi.
    /// </summary>
    private static IEnumerable<DateOnly> DueDates(RecurringItemView item, DateOnly today, DateOnly end, ForecastRules rules)
    {
        var dates = RecurringSchedule.DueDates(item);
        if (!KeepsOverdue(item))
        {
            var cutoff = today.AddDays(-rules.OverdueGraceDays);
            dates = dates.SkipWhile(d => d < cutoff);
        }

        return dates.TakeWhile(d => d <= end);
    }

    private static bool KeepsOverdue(RecurringItemView item) => item.CountsOccurrences && item.Direction == RecurringDirection.Outflow;

    /// <summary>Lần quá hạn chưa trả: khoản thường coi như trả hôm nay (thận trọng); khoản trả góp theo số kỳ (S1) dời sang ngày mai.</summary>
    private static DateOnly OverdueDate(RecurringItemView item, DateOnly due, DateOnly today)
        => due >= today ? due : KeepsOverdue(item) ? today.AddDays(1) : today;

    private static (long Expected, long Low, long High) FlexibleSpend(CashflowInput input, DateOnly date, int multiplierPercent)
    {
        var expected = Scale(input.Baseline.ExpectedOn(date), multiplierPercent);
        var low = Scale(input.Baseline.LowOn(date), multiplierPercent);
        var high = Scale(input.Baseline.HighOn(date), multiplierPercent);
        if (date != input.Today)
        {
            return (expected, low, high);
        }

        // Phần đã chi hôm nay đã nằm trong số dư hiện tại: chỉ trừ phần còn lại của ngày.
        var spent = input.SpentTodayFlexibleCents;
        return (Math.Max(0, expected - spent), Math.Max(0, low - spent), Math.Max(0, high - spent));
    }

    private static int MultiplierOn(DateOnly date, IReadOnlyList<ForecastHoliday> holidays)
        => holidays.Where(h => date >= h.Start && date <= h.End).Select(h => h.MultiplierPercent).DefaultIfEmpty(NeutralMultiplierPercent).Max();

    private static long Scale(long amount, int percent) => (long)Math.Round(amount * percent / 100.0, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<CashflowCauseDto> MainCauses(IReadOnlyList<CashflowEvent> events, long flexibleTotal)
    {
        var causes = events
            .Where(e => e.AmountCents < 0 && e.Band == CashflowBand.All)
            .GroupBy(e => (e.Kind, e.Label))
            .Select(g => new CashflowCauseDto(
                g.Key.Kind, g.Key.Label, -g.Sum(e => e.AmountCents), g.Key.Kind == CashflowEventKinds.LoanOutflow ? CashflowSources.UserEntered : null))
            .ToList();
        if (flexibleTotal > 0)
        {
            causes.Add(new CashflowCauseDto(CashflowEventKinds.Flexible, "Chi tiêu hằng ngày", flexibleTotal));
        }

        return causes.OrderByDescending(c => c.AmountCents).Take(MaxCauses).ToList();
    }
}
