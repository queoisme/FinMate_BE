using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports;

public class StatisticalSpendingForecaster : ISpendingForecaster
{
    /// <summary>Dưới ngưỡng này thì run-rate tháng hiện tại quá mỏng, nghiêng về lịch sử.</summary>
    private const int MinDaysForCurrentMonthRunRate = 7;

    private const int HistoryMonths = 2;

    private readonly IReportRepository _reportRepository;
    private readonly Func<DateTimeOffset> _now;

    public StatisticalSpendingForecaster(IReportRepository reportRepository)
        : this(reportRepository, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// Cho phép test cố định "hôm nay". Kết quả dự báo phụ thuộc ngày trong tháng, nên nếu
    /// lấy thẳng <c>DateTimeOffset.UtcNow</c> thì test chỉ đúng vào một số ngày nhất định —
    /// loại test tự vô hiệu hóa mà không ai nhận ra.
    /// </summary>
    internal StatisticalSpendingForecaster(IReportRepository reportRepository, Func<DateTimeOffset> now)
    {
        _reportRepository = reportRepository;
        _now = now;
    }

    public async Task<SpendingForecastDto> ForecastCurrentMonthAsync(Guid userId, CancellationToken ct = default)
    {
        var now = _now();
        var (periodStart, periodEnd) = VietnamTime.MonthRange(now);
        var (year, month) = VietnamTime.YearMonthOf(now);

        var today = VietnamTime.DateOf(now);
        var monthFirstDay = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var daysElapsed = today.Day;

        var historyStart = monthFirstDay.AddMonths(-HistoryMonths);
        var points = await _reportRepository.GetDailySpendAsync(userId, historyStart, today, ct);

        var currentMonthDays = points
            .Where(p => p.Date >= monthFirstDay)
            .ToDictionary(p => p.Date, p => p.SpentCents);

        var spentSoFar = currentMonthDays.Values.Sum();

        // Ngày không có giao dịch vẫn là ngày chi 0 — bỏ qua chúng sẽ thổi phồng run-rate
        // thành "chi tiêu mỗi ngày có tiêu", không phải "chi tiêu mỗi ngày".
        var currentMonthSeries = Enumerable.Range(1, daysElapsed)
            .Select(d => (double)currentMonthDays.GetValueOrDefault(new DateOnly(year, month, d), 0))
            .ToArray();

        var historySeries = points
            .Where(p => p.Date < monthFirstDay)
            .Select(p => (double)p.SpentCents)
            .ToArray();

        var basedOnDays = currentMonthSeries.Length + historySeries.Length;

        double dailyRate;
        if (currentMonthSeries.Length >= MinDaysForCurrentMonthRunRate)
        {
            dailyRate = Statistics.Median(Statistics.TrimUpperOutliers(currentMonthSeries));
        }
        else if (historySeries.Length > 0)
        {
            dailyRate = Statistics.Median(Statistics.TrimUpperOutliers(historySeries));
        }
        else if (currentMonthSeries.Length > 0)
        {
            dailyRate = Statistics.Median(currentMonthSeries);
        }
        else
        {
            dailyRate = 0;
        }

        // Phần đã tiêu là số thật, chỉ ngoại suy phần ngày còn lại — không nhân run-rate cho
        // cả tháng rồi vứt bỏ dữ liệu thực tế đã có.
        var remainingDays = Math.Max(0, daysInMonth - daysElapsed);
        var projected = spentSoFar + (long)Math.Round(dailyRate * remainingDays);

        return new SpendingForecastDto(
            periodStart.ToOffset(VietnamTime.Offset),
            periodEnd.ToOffset(VietnamTime.Offset),
            spentSoFar,
            projected,
            daysElapsed,
            daysInMonth,
            basedOnDays,
            ConfidenceFor(basedOnDays));
    }

    private static string ConfidenceFor(int basedOnDays) => basedOnDays switch
    {
        >= 45 => "high",
        >= 14 => "medium",
        _ => "low",
    };
}
