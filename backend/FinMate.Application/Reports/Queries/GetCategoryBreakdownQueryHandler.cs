using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetCategoryBreakdownQueryHandler : IGetCategoryBreakdownQueryHandler
{
    private const string UncategorizedName = "Chưa phân loại";

    private readonly IReportRepository _reportRepository;

    public GetCategoryBreakdownQueryHandler(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
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
        var spend = await _reportRepository.GetCategorySpendAsync(query.UserId, monthFirst, monthLast, ct);
        var total = spend.Sum(s => s.SpentCents);

        var items = spend
            .Select(s => new CategoryBreakdownItemDto(
                s.CategoryId,
                s.CategoryName ?? UncategorizedName,
                s.CategorySlug,
                s.SpentCents,
                Percent(s.SpentCents, total)))
            .ToList();

        DistributeRoundingRemainder(items, total);

        return new CategoryBreakdownDto(
            periodStart.ToOffset(VietnamTime.Offset),
            periodEnd.ToOffset(VietnamTime.Offset),
            total,
            items);
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
