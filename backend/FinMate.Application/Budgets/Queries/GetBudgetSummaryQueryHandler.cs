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
            ? VietnamTime.MonthRange(query.Year.Value, query.Month.Value)
            : VietnamTime.MonthRange(DateTimeOffset.UtcNow);

        // Lấy năm/tháng theo giờ VN, không lấy từ biểu diễn UTC của periodStart: mốc đầu
        // tháng 10 giờ VN nằm ở 30/09 theo UTC, dùng thẳng sẽ ra key của tháng trước.
        var (year, month) = VietnamTime.YearMonthOf(periodStart);
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

        var summary = new BudgetSummaryDto(
            // Trả mốc chu kỳ theo giờ VN: cùng mốc thời gian, nhưng client đọc "01/09" thay vì
            // "31/08 17:00Z" — biểu diễn UTC chỉ cần cho tầng lưu trữ (xem VietnamTime).
            periodStart.ToOffset(VietnamTime.Offset),
            periodEnd.ToOffset(VietnamTime.Offset),
            items.SingleOrDefault(i => i.CategoryId is null),
            items.Where(i => i.CategoryId is not null).ToList());

        await _cache.SetAsync(cacheKey, summary, CacheKeys.BudgetSummaryTtl, ct);

        return summary;
    }
}
