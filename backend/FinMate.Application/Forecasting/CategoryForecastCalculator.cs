using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Ngưỡng dự báo theo danh mục, lấy từ cấu hình hệ thống <c>forecast</c>.</summary>
public sealed record CategoryForecastRules(int Months, int MinMonths, int ChangePercent, long RoundingCents)
{
    public static CategoryForecastRules From(Admin.SystemConfig.ForecastSettings s)
        => new(s.CategoryForecastMonths, s.CategoryForecastMinMonths, s.CategoryChangePercent, s.BudgetSuggestionRoundingCents);
}

/// <param name="CategoryId">Null = giao dịch chưa phân loại (không đề xuất hạn mức được).</param>
/// <param name="ChangePercent">So với tháng trước; null khi tháng trước không chi gì.</param>
public sealed record CategoryForecastItem(
    Guid? CategoryId,
    long ProjectedCents,
    string Basis,
    ForecastConfidence Confidence,
    long PreviousMonthCents,
    long? CurrentLimitCents,
    int? ChangePercent,
    bool ChangedSignificantly,
    long? SuggestedLimitCents);

public sealed record CategoryForecastResult(bool EnoughHistory, int MonthsWithData, IReadOnlyList<CategoryForecastItem> Items);

/// <summary>
/// Dự báo chi theo danh mục cho một tháng tới (Flow 6, trang 2). Hàm thuần. Xu hướng gần đây = trung bình có trọng số các tháng trọn vẹn gần nhất
/// (tháng gần nhất nặng nhất), chỉ tính từ tháng đầu tiên user có dữ liệu — tháng trước khi dùng app không phải "tháng không chi".
/// Thiếu lịch sử (BR-06.1): hạn mức ngân sách của danh mục, không có thì trung bình theo ngày của dữ liệu đang có quy ra tháng; cả hai mang độ tin cậy thấp.
/// Chưa điều chỉnh mùa vụ (cần trên một năm dữ liệu).
/// </summary>
public static class CategoryForecastCalculator
{
    /// <param name="history">Giao dịch từ đầu tháng thứ <c>Months</c> trước tháng này tới hết hôm qua.</param>
    /// <param name="excludedIds">Giao dịch không dùng làm dữ liệu (bất thường chưa được xác nhận bình thường).</param>
    /// <param name="categoryLimits">Hạn mức ngân sách tháng theo danh mục (không gồm ngân sách tổng).</param>
    public static CategoryForecastResult Compute(
        IReadOnlyList<ForecastTransaction> history,
        IReadOnlySet<Guid> excludedIds,
        IReadOnlyDictionary<Guid, long> categoryLimits,
        DateOnly today,
        DateOnly targetMonth,
        CategoryForecastRules rules)
    {
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var months = Enumerable.Range(1, rules.Months).Select(i => currentMonth.AddMonths(-i)).ToList();   // [0] = tháng trước
        var dataMonths = months.Where(m => history.Any(t => InMonth(t.Date, m))).ToList();
        var enough = dataMonths.Count >= rules.MinMonths;
        var usedMonths = enough ? months.Take(months.IndexOf(dataMonths[^1]) + 1).ToList() : [];

        var usable = history
            .Where(t => t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !excludedIds.Contains(t.Id))
            .ToList();
        var categories = usable.Select(t => t.CategoryId).Concat(categoryLimits.Keys.Select(k => (Guid?)k)).Distinct();
        var daysInTarget = DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month);

        var items = new List<CategoryForecastItem>();
        foreach (var categoryId in categories)
        {
            var spending = usable.Where(t => t.CategoryId == categoryId).ToList();
            long? limit = categoryId is { } id && categoryLimits.TryGetValue(id, out var l) ? l : null;
            var (projected, basis) = enough
                ? (Weighted(spending, usedMonths), SpendingBaseline.BasisHistory)
                : Fallback(spending, history, limit, today, daysInTarget);
            if (projected <= 0 && limit is null)
            {
                continue;
            }

            var previous = spending.Where(t => InMonth(t.Date, months[0])).Sum(t => t.AmountCents);
            items.Add(Item(categoryId, projected, basis, enough, previous, limit, rules));
        }

        return new CategoryForecastResult(enough, dataMonths.Count, items.OrderByDescending(i => i.ProjectedCents).ToList());
    }

    /// <summary>Làm tròn LÊN bội số <paramref name="step"/> — hạn mức đề xuất không được thấp hơn mức dự kiến.</summary>
    public static long RoundUp(long amount, long step)
        => amount <= 0 ? 0 : step <= 1 ? amount : ((amount + step - 1) / step) * step;

    private static long Weighted(IReadOnlyList<ForecastTransaction> spending, IReadOnlyList<DateOnly> months)
    {
        long weightedSum = 0;
        long weights = 0;
        for (var i = 0; i < months.Count; i++)
        {
            var weight = months.Count - i;
            weightedSum += weight * spending.Where(t => InMonth(t.Date, months[i])).Sum(t => t.AmountCents);
            weights += weight;
        }

        return weights == 0 ? 0 : (long)Math.Round((double)weightedSum / weights, MidpointRounding.AwayFromZero);
    }

    private static (long Projected, string Basis) Fallback(
        IReadOnlyList<ForecastTransaction> spending, IReadOnlyList<ForecastTransaction> history, long? limit, DateOnly today, int daysInTarget)
    {
        if (limit is { } budget)
        {
            return (budget, SpendingBaseline.BasisBudget);
        }

        if (spending.Count == 0)
        {
            return (0, SpendingBaseline.BasisNone);
        }

        var span = Math.Max(1, today.DayNumber - history.Min(t => t.Date).DayNumber);
        var perDay = (double)spending.Sum(t => t.AmountCents) / span;
        return ((long)Math.Round(perDay * daysInTarget, MidpointRounding.AwayFromZero), SpendingBaseline.BasisAverage);
    }

    private static CategoryForecastItem Item(
        Guid? categoryId, long projected, string basis, bool enough, long previous, long? limit, CategoryForecastRules rules)
    {
        int? change = previous > 0 ? (int)Math.Round((projected - previous) * 100.0 / previous, MidpointRounding.AwayFromZero) : null;
        var significant = change is { } c ? Math.Abs(c) >= rules.ChangePercent : projected > 0;
        long? suggested = categoryId is null ? null : RoundUp(projected, rules.RoundingCents);
        return new CategoryForecastItem(
            categoryId, projected, basis, enough ? ForecastConfidence.Normal : ForecastConfidence.Low,
            previous, limit, change, significant, suggested);
    }

    private static bool InMonth(DateOnly date, DateOnly monthStart) => date.Year == monthStart.Year && date.Month == monthStart.Month;
}
