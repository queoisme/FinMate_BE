using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetCategoryBreakdownQueryHandler : IGetCategoryBreakdownQueryHandler
{
    private const string UncategorizedName = "Chưa phân loại";

    private readonly IReportRepository _reportRepository;
    private readonly IBudgetRepository? _budgets;   // null (test cũ): không có thông tin ngân sách

    public GetCategoryBreakdownQueryHandler(IReportRepository reportRepository, IBudgetRepository? budgets = null)
    {
        _reportRepository = reportRepository;
        _budgets = budgets;
    }

    public async Task<CategoryBreakdownDto> HandleAsync(GetCategoryBreakdownQuery query, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var (year, month) = query.Year is not null && query.Month is not null
            ? (query.Year.Value, query.Month.Value)
            : VietnamTime.YearMonthOf(now);

        var (periodStart, periodEnd) = VietnamTime.MonthRange(year, month);
        var monthFirst = new DateOnly(year, month, 1);
        var monthLast = monthFirst.AddMonths(1).AddDays(-1);

        // Tính live chứ không đọc daily_summaries: bảng đó chỉ giữ top category, không đủ để
        // dựng bản phân tích đầy đủ theo từng danh mục.
        var spend = query.Type == "income"
            ? await _reportRepository.GetCategoryIncomeAsync(query.UserId, monthFirst, monthLast, ct)
            : await _reportRepository.GetCategorySpendAsync(query.UserId, monthFirst, monthLast, ct);
        var total = spend.Sum(s => s.SpentCents);

        // Tháng liền trước, cùng định nghĩa: để hiện "tiết kiệm X so với tháng trước".
        var prevFirst = monthFirst.AddMonths(-1);
        var prevLast = monthFirst.AddDays(-1);
        var prevSpend = query.Type == "income"
            ? await _reportRepository.GetCategoryIncomeAsync(query.UserId, prevFirst, prevLast, ct)
            : await _reportRepository.GetCategorySpendAsync(query.UserId, prevFirst, prevLast, ct);
        var prevByCategory = prevSpend.ToDictionary(s => s.CategoryId ?? Guid.Empty, s => s.SpentCents);   // Guid.Empty = chưa phân loại

        var budgetOf = query.Type == "income" || _budgets is null
            ? new Dictionary<Guid, (long Limit, long Spent)>()
            : await BudgetsOfMonthAsync(query.UserId, periodStart, ct);

        var items = spend
            .Select(s =>
            {
                var prev = prevByCategory.GetValueOrDefault(s.CategoryId ?? Guid.Empty);
                var saved = query.Type == "income" ? 0 : Math.Max(0, prev - s.SpentCents);
                (long Limit, long Spent)? budget = s.CategoryId is { } id && budgetOf.TryGetValue(id, out var b) ? b : null;
                return new CategoryBreakdownItemDto(
                    s.CategoryId,
                    s.CategoryName ?? UncategorizedName,
                    s.CategorySlug,
                    s.SpentCents,
                    Percent(s.SpentCents, total),
                    prev,
                    saved,
                    s.TransactionCount,
                    budget?.Limit,
                    budget is null ? null : s.SpentCents);
            })
            .ToList();

        DistributeRoundingRemainder(items, total);

        return new CategoryBreakdownDto(
            periodStart.ToOffset(VietnamTime.Offset),
            periodEnd.ToOffset(VietnamTime.Offset),
            total,
            items);
    }

    /// <summary>
    /// Hạn mức ngân sách THÁNG theo danh mục đúng tháng được hỏi: ưu tiên ảnh chụp <c>budget_periods</c> của tháng đó (tháng cũ giữ hạn mức lúc đó), không có thì
    /// dùng hạn mức hiện tại của ngân sách nếu ngân sách đã tồn tại trước khi tháng kết thúc.
    /// </summary>
    private async Task<Dictionary<Guid, (long Limit, long Spent)>> BudgetsOfMonthAsync(Guid userId, DateTimeOffset periodStart, CancellationToken ct)
    {
        var (_, periodEnd) = VietnamTime.MonthRange(VietnamTime.ToLocal(periodStart).Year, VietnamTime.ToLocal(periodStart).Month);
        var budgets = (await _budgets!.GetListForUserAsync(userId, ct))
            .Where(b => b.CategoryId is not null && b.PeriodType == Domain.Enums.BudgetPeriodType.Monthly)
            .ToList();
        var periods = (await _budgets.GetPeriodsForUserAsync(userId, periodStart, ct)).ToDictionary(p => p.BudgetId);

        var result = new Dictionary<Guid, (long, long)>();
        foreach (var budget in budgets)
        {
            if (periods.TryGetValue(budget.Id, out var period))
            {
                result[budget.CategoryId!.Value] = (period.LimitCents, period.SpentCents);
            }
            else if (budget.CreatedAt < periodEnd)
            {
                result[budget.CategoryId!.Value] = (budget.LimitCents, 0);
            }
        }

        return result;
    }

    private static int Percent(long spent, long total)
        => total <= 0 ? 0 : (int)(spent * 100 / total);

    /// <summary>
    /// Làm tròn xuống từng mục khiến tổng phần trăm thường ra 97–99. Dồn phần thiếu vào mục
    /// lớn nhất để biểu đồ tròn không bị hở một mẩu mà không mục nào nhận.
    /// </summary>
    private static void DistributeRoundingRemainder(List<CategoryBreakdownItemDto> items, long total)
    {
        if (items.Count == 0 || total <= 0)
        {
            return;
        }

        var remainder = 100 - items.Sum(i => i.Percent);
        if (remainder <= 0)
        {
            return;
        }

        var largestIndex = 0;
        for (var i = 1; i < items.Count; i++)
        {
            if (items[i].SpentCents > items[largestIndex].SpentCents)
            {
                largestIndex = i;
            }
        }

        items[largestIndex] = items[largestIndex] with { Percent = items[largestIndex].Percent + remainder };
    }
}
