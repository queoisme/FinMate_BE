using FinMate.Application.FinanceMethods;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public sealed record SpendableAccount(Guid Id, string Name, long BalanceCents);

/// <summary>Một khoản thu/chi định kỳ rơi vào kỳ tính, số tiền luôn dương.</summary>
public sealed record UpcomingItemDto(DateOnly Date, string Label, long AmountCents, Guid? RecurringItemId);

public sealed record SpendableInput(
    DateOnly Today,
    IReadOnlyList<SpendableAccount> Accounts,
    IReadOnlyList<RecurringItemView> ActiveItems,
    IReadOnlyList<ForecastTransaction> RecentTransactions,
    long GoalSetAsideCents,
    IReadOnlyList<SpendableGoalDto> Goals,
    long ReserveCents,
    ForecastRules Rules,
    long IrregularIncomeCents = 0);

public static class SpendablePeriodBasis
{
    public const string NextIncome = "next_income";
    public const string MonthEnd = "month_end";
}

/// <summary>
/// Số tiền có thể chi (sơ đồ "Xem số tiền có thể chi"). Kết quả ≤ 0 thì <see cref="SpendableCents"/> = 0 và <see cref="ShortfallCents"/>
/// là số còn thiếu; không bao giờ trả số âm cho "có thể chi".
/// </summary>
public sealed record SafeToSpendDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int DaysRemaining,
    string PeriodBasis,
    long BalanceCents,
    long ExpectedIncomeCents,
    long UpcomingBillsCents,
    long GoalSetAsideCents,
    long ReserveCents,
    long SpendableCents,
    long PerDayCents,
    long ShortfallCents,
    IReadOnlyList<SpendableAccount> Accounts,
    IReadOnlyList<UpcomingItemDto> Incomes,
    IReadOnlyList<UpcomingItemDto> Bills,
    IReadOnlyList<SpendableGoalDto> Goals)
{
    /// <summary>Phase 84 (BR-LH-08): độ phủ dữ liệu. Công thức này không có độ tin cậy nên chỉ thêm trường này (D11).</summary>
    public ListenerHealth.DataCoverageSummaryDto? DataCoverage { get; init; }
}

public static class SpendableCalculator
{
    /// <summary>Lương kế tiếp xa hơn chừng này thì kỳ tính dừng ở cuối tháng, để con số "mỗi ngày" không bị chia cho một kỳ quá dài.</summary>
    private const int MaxIncomePeriodDays = 45;

    public const string IrregularIncomeLabel = "Thu nhập dự kiến (mức thận trọng)";

    public static SafeToSpendDto Compute(SpendableInput input)
    {
        var (end, basis) = Period(input);
        var events = CashflowEngine.RecurringEvents(input.ActiveItems, input.RecentTransactions, input.Today, end, input.Rules);
        var incomes = events.Where(e => e.AmountCents > 0).ToList();
        if (input.IrregularIncomeCents > 0)
        {
            // Không có khoản thu định kỳ: cộng thu nhập không đều ở mức thận trọng (sơ đồ trang 10), đã trừ phần nhận trong tháng.
            incomes.Add(new CashflowEvent(input.Today, input.IrregularIncomeCents, IrregularIncomeLabel, CashflowEventKinds.RecurringInflow, null));
        }

        var bills = events.Where(e => e.AmountCents < 0).ToList();

        var balance = input.Accounts.Sum(a => a.BalanceCents);
        var income = incomes.Sum(e => e.AmountCents);
        var billsTotal = -bills.Sum(e => e.AmountCents);
        var result = balance + income - billsTotal - input.GoalSetAsideCents - input.ReserveCents;
        var daysRemaining = end.DayNumber - input.Today.DayNumber + 1;

        var spendable = Math.Max(0, result);
        return new SafeToSpendDto(
            input.Today, end, daysRemaining, basis,
            balance, income, billsTotal, input.GoalSetAsideCents, input.ReserveCents,
            spendable,
            spendable / daysRemaining,
            Math.Max(0, -result),
            input.Accounts,
            ToDto(incomes),
            ToDto(bills),
            input.Goals);
    }

    /// <summary>Kỳ tính (BR-03.1): hôm nay tới hết ngày trước lần nhận thu nhập định kỳ kế tiếp; không biết thì tới cuối tháng.</summary>
    private static (DateOnly End, string Basis) Period(SpendableInput input)
    {
        var horizon = input.Today.AddDays(MaxIncomePeriodDays);
        var nextIncome = input.ActiveItems
            .Where(i => i.Direction == RecurringDirection.Inflow)
            .SelectMany(i => RecurringSchedule.Occurrences(i, input.Today.AddDays(1), horizon))
            .DefaultIfEmpty()
            .Min();

        if (nextIncome != default)
        {
            return (nextIncome.AddDays(-1), SpendablePeriodBasis.NextIncome);
        }

        var monthEnd = new DateOnly(input.Today.Year, input.Today.Month, DateTime.DaysInMonth(input.Today.Year, input.Today.Month));
        return (monthEnd, SpendablePeriodBasis.MonthEnd);
    }

    private static IReadOnlyList<UpcomingItemDto> ToDto(IEnumerable<CashflowEvent> events)
        => events.OrderBy(e => e.Date).Select(e => new UpcomingItemDto(e.Date, e.Label, Math.Abs(e.AmountCents), e.RecurringItemId)).ToList();
}
