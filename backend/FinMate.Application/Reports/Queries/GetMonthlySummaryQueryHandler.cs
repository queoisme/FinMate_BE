using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetMonthlySummaryQueryHandler : IGetMonthlySummaryQueryHandler
{
    private readonly IReportRepository _reportRepository;

    public GetMonthlySummaryQueryHandler(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public async Task<MonthlySummaryDto> HandleAsync(GetMonthlySummaryQuery query, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var (year, month) = query.Year is not null && query.Month is not null
            ? (query.Year.Value, query.Month.Value)
            : VietnamTime.YearMonthOf(now);

        var (periodStart, periodEnd) = VietnamTime.MonthRange(year, month);
        var monthFirst = new DateOnly(year, month, 1);
        var monthLast = monthFirst.AddMonths(1).AddDays(-1);

        var (spent, income, count) = await ReadRangeAsync(query.UserId, monthFirst, monthLast, ct);

        var prevFirst = monthFirst.AddMonths(-1);
        var prevLast = monthFirst.AddDays(-1);
        var (prevSpent, _, _) = await ReadRangeAsync(query.UserId, prevFirst, prevLast, ct);

        double? changePercent = prevSpent > 0
            ? Math.Round((spent - prevSpent) * 100.0 / prevSpent, 1)
            : null;

        return new MonthlySummaryDto(
            periodStart.ToOffset(VietnamTime.Offset),
            periodEnd.ToOffset(VietnamTime.Offset),
            spent,
            income,
            income - spent,
            count,
            prevSpent,
            changePercent);
    }

    /// <summary>
    /// Ưu tiên <c>daily_summaries</c> cho ngày nào đã được chốt, và lấy số liệu live cho mọi
    /// ngày còn lại.
    ///
    /// Không thể suy "không có dòng summary" thành "ngày đó không tiêu gì": nó cũng đúng khi
    /// <c>DailySummaryJob</c> chưa từng chạy cho ngày đó — đúng trạng thái của một môi trường
    /// vừa dựng, hoặc bất kỳ ngày nào job lỗi. Bản đầu chỉ cộng summary nên báo cáo âm thầm
    /// thiếu dữ liệu, và smoke test đã bắt được: tổng tháng ra 300k trong khi breakdown và
    /// timeline cùng ra 900k.
    ///
    /// Cái giá là một truy vấn live có giới hạn (tối đa 31 ngày của MỘT user, đi qua index
    /// <c>(user_id, transacted_at, id)</c>) — rẻ hơn nhiều so với một con số sai.
    /// </summary>
    private async Task<(long Spent, long Income, int Count)> ReadRangeAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var summaries = await _reportRepository.GetDailySummariesAsync(userId, from, to, ct);
        var summarisedDates = summaries.Select(s => s.SummaryDate).ToHashSet();

        var live = await _reportRepository.GetDailySpendAsync(userId, from, to, ct);

        var spent = summaries.Sum(s => s.TotalSpentCents);
        var income = summaries.Sum(s => s.TotalIncomeCents);
        var count = summaries.Sum(s => s.TransactionCount);

        foreach (var point in live.Where(p => !summarisedDates.Contains(p.Date)))
        {
            spent += point.SpentCents;
            income += point.IncomeCents;
            count += point.TransactionCount;
        }

        return (spent, income, count);
    }
}
