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
    /// Đọc từ daily_summaries (aggregate dựng sẵn) cho các ngày đã chốt, cộng thêm phần tính
    /// live cho hôm nay trở đi. DailySummaryJob chỉ chạy tới hết hôm qua, không cộng phần
    /// live thì user xem "tháng này" sẽ luôn thiếu đúng ngày hôm nay.
    /// </summary>
    private async Task<(long Spent, long Income, int Count)> ReadRangeAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var today = VietnamTime.Today();
        var summarisedTo = to < today ? to : today.AddDays(-1);

        long spent = 0, income = 0;
        var count = 0;

        if (summarisedTo >= from)
        {
            var summaries = await _reportRepository.GetDailySummariesAsync(userId, from, summarisedTo, ct);
            spent += summaries.Sum(s => s.TotalSpentCents);
            income += summaries.Sum(s => s.TotalIncomeCents);
            count += summaries.Sum(s => s.TransactionCount);
        }

        var liveFrom = summarisedTo >= from ? summarisedTo.AddDays(1) : from;
        if (liveFrom <= to)
        {
            var live = await _reportRepository.GetDailySpendAsync(userId, liveFrom, to, ct);
            spent += live.Sum(p => p.SpentCents);
            income += live.Sum(p => p.IncomeCents);
            count += live.Sum(p => p.TransactionCount);
        }

        return (spent, income, count);
    }
}
