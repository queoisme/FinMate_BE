using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public static class BudgetRiskStatus
{
    public const string Ok = "ok";
    public const string AtRisk = "at_risk";
    public const string Over = "over";
}

public static class BudgetRiskBasis
{
    /// <summary>Nhịp chi của chính tháng này.</summary>
    public const string Pace = "pace";

    /// <summary>Thói quen của danh mục trong cửa sổ lịch sử (đầu tháng chưa đủ ngày để tin nhịp chi).</summary>
    public const string History = "history";
}

/// <summary>Một ngân sách của kỳ hiện tại, đủ để tính dự báo. <paramref name="CategoryId"/> null = ngân sách tổng.</summary>
public sealed record BudgetRiskInput(
    Guid BudgetId,
    Guid? CategoryId,
    string? CategoryName,
    long LimitCents,
    long SpentCents,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

/// <param name="DaysRemaining">Số ngày còn lại của kỳ, gồm hôm nay.</param>
/// <param name="MaxDailySpendCents">Chi tối đa mỗi ngày còn lại để không vượt hạn mức (0 nếu đã vượt).</param>
public sealed record BudgetForecastDto(
    Guid BudgetId,
    Guid? CategoryId,
    string? CategoryName,
    long LimitCents,
    long SpentCents,
    long ProjectedCents,
    int ProjectedPercent,
    string Status,
    long MaxDailySpendCents,
    int DaysRemaining,
    string Basis);

/// <summary>
/// Trang 6 của sơ đồ: "Ước tính tổng chi đến cuối kỳ theo nhịp chi và thói quen" → "Tính khả năng vượt ngân sách". Dự kiến = đã chi
/// + chi linh hoạt mỗi ngày × số ngày còn lại sau hôm nay + khoản chi định kỳ của danh mục còn chưa trả trong kỳ. Chi linh hoạt không
/// gồm giao dịch khớp khoản định kỳ, để khoản đó không bị tính hai lần (một lần trong nhịp chi, một lần ở ngày đến hạn).
/// </summary>
public static class BudgetRiskCalculator
{
    public static BudgetForecastDto Compute(
        BudgetRiskInput budget,
        IReadOnlyList<ForecastTransaction> history,
        IReadOnlyList<RecurringItemView> activeItems,
        IReadOnlyList<CashflowEvent> upcomingRecurring,
        IReadOnlyDictionary<Guid, Guid?> itemCategories,
        DateOnly today,
        ForecastRules rules,
        int minElapsedDays)
    {
        var flexible = history
            .Where(t => t.Type == TransactionType.Debit
                        && !t.ExcludedFromForecast
                        && InScope(budget, t.CategoryId)
                        && !RecurringMatcher.MatchesAny(t, activeItems, rules))
            .ToList();

        var elapsed = today.DayNumber - budget.PeriodStart.DayNumber + 1;
        var futureDays = Math.Max(0, budget.PeriodEnd.DayNumber - today.DayNumber);
        var (dailyRate, basis) = DailyRate(flexible, budget.PeriodStart, today, elapsed, minElapsedDays, rules.HistoryWindowDays);

        var recurringAhead = -upcomingRecurring
            .Where(e => e.AmountCents < 0 && e.Date > today && e.Date <= budget.PeriodEnd
                        && e.RecurringItemId is { } id && InScope(budget, itemCategories.GetValueOrDefault(id)))
            .Sum(e => e.AmountCents);

        var projected = budget.SpentCents + (long)Math.Round(dailyRate * futureDays) + recurringAhead;
        var daysRemaining = futureDays + 1;
        var status = budget.SpentCents > budget.LimitCents ? BudgetRiskStatus.Over
            : projected > budget.LimitCents ? BudgetRiskStatus.AtRisk
            : BudgetRiskStatus.Ok;

        return new BudgetForecastDto(
            budget.BudgetId, budget.CategoryId, budget.CategoryName, budget.LimitCents, budget.SpentCents,
            projected,
            budget.LimitCents > 0 ? (int)Math.Min(int.MaxValue, projected * 100 / budget.LimitCents) : 0,
            status,
            Math.Max(0, budget.LimitCents - budget.SpentCents) / daysRemaining,
            daysRemaining,
            basis);
    }

    /// <summary>Có nên gửi cảnh báo sớm không (BR-04.2): dự kiến vượt và còn đủ ngày để kịp điều chỉnh.</summary>
    public static bool ShouldWarn(BudgetForecastDto forecast, int minDaysRemaining)
        => forecast.Status == BudgetRiskStatus.AtRisk && forecast.DaysRemaining >= minDaysRemaining;

    private static bool InScope(BudgetRiskInput budget, Guid? categoryId) => budget.CategoryId is null || budget.CategoryId == categoryId;

    private static (double Rate, string Basis) DailyRate(
        IReadOnlyList<ForecastTransaction> flexible, DateOnly periodStart, DateOnly today, int elapsed, int minElapsedDays, int historyDays)
    {
        if (elapsed >= minElapsedDays)
        {
            var thisPeriod = flexible.Where(t => t.Date >= periodStart && t.Date <= today).Sum(t => t.AmountCents);
            return ((double)thisPeriod / elapsed, BudgetRiskBasis.Pace);
        }

        var before = flexible.Where(t => t.Date < periodStart).ToList();
        if (before.Count == 0)
        {
            var soFar = flexible.Where(t => t.Date >= periodStart && t.Date <= today).Sum(t => t.AmountCents);
            return ((double)soFar / elapsed, BudgetRiskBasis.Pace);
        }

        var span = Math.Min(historyDays, periodStart.DayNumber - before.Min(t => t.Date).DayNumber);
        return ((double)before.Sum(t => t.AmountCents) / Math.Max(1, span), BudgetRiskBasis.History);
    }
}
