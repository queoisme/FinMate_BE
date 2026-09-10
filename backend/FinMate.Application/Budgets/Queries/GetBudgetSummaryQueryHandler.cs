using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Budgets.Queries;

public class GetBudgetSummaryQueryHandler : IGetBudgetSummaryQueryHandler
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICacheService _cache;

    public GetBudgetSummaryQueryHandler(IBudgetRepository budgetRepository, ICacheService cache)
    {
        _budgetRepository = budgetRepository;
        _cache = cache;
    }

    public async Task<BudgetSummaryDto> HandleAsync(GetBudgetSummaryQuery query, CancellationToken ct = default)
    {
        var (periodStart, periodEnd) = query.Year is not null && query.Month is not null
            ? BudgetCalendar.MonthlyPeriod(query.Year.Value, query.Month.Value)
            : BudgetCalendar.MonthlyPeriod(DateTimeOffset.UtcNow);

        // Lấy năm/tháng theo giờ VN, không lấy từ biểu diễn UTC của periodStart: mốc đầu
        // tháng 10 giờ VN nằm ở 30/09 theo UTC, dùng thẳng sẽ ra key của tháng trước.
        var (year, month) = BudgetCalendar.VietnamYearMonth(periodStart);
        var cacheKey = CacheKeys.BudgetSummary(query.UserId, year, month);
        var cached = await _cache.GetAsync<BudgetSummaryDto>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var budgets = await _budgetRepository.GetListForUserAsync(query.UserId, ct);
        var periods = await _budgetRepository.GetPeriodsForUserAsync(query.UserId, periodStart, ct);
        var periodByBudget = periods.ToDictionary(p => p.BudgetId);

        var items = new List<BudgetSummaryItemDto>(budgets.Count);
        foreach (var budget in budgets)
        {
            // Chưa có period nghĩa là chu kỳ đó user chưa tiêu đồng nào cho budget này —
            // period chỉ được tạo lazily khi có giao dịch đầu tiên.
            var spent = periodByBudget.TryGetValue(budget.Id, out var period) ? period.SpentCents : 0;
            var limit = period?.LimitCents ?? budget.LimitCents;

            items.Add(new BudgetSummaryItemDto(
                budget.Id,
                budget.CategoryId,
                budget.Category?.Name,
                budget.Category?.Slug,
                limit,
                spent,
                Math.Max(0, limit - spent),
                BudgetMapper.PercentUsed(spent, limit),
                spent > limit));
        }

        // Budget tổng (CategoryId null) bao trùm mọi category nên cộng nó vào Total sẽ tính
        // trùng — Total chỉ tổng hợp các budget theo category.
        var categoryItems = items.Where(i => i.CategoryId is not null).ToList();

        var summary = new BudgetSummaryDto(
            periodStart,
            periodEnd,
            categoryItems.Sum(i => i.LimitCents),
            categoryItems.Sum(i => i.SpentCents),
            items);

        await _cache.SetAsync(cacheKey, summary, CacheKeys.BudgetSummaryTtl, ct);

        return summary;
    }
}
