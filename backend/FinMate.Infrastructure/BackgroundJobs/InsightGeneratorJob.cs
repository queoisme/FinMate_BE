using FinMate.Application.Common;
using FinMate.Application.Reports;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Sinh spending insight mỗi đêm (ARCHITECTURE.md §5, 02:00). Mỗi loại insight có ngưỡng
/// riêng để không spam những thứ hiển nhiên, và luôn kiểm tra tồn tại trước khi ghi để chạy
/// lại nhiều đêm không sinh trùng.
/// </summary>
public class InsightGeneratorJob
{
    /// <summary>Dưới mức này thì chênh lệch tháng chỉ là dao động thường ngày, không đáng báo.</summary>
    private const double VsLastMonthThresholdPercent = 15;

    private const int ActivityLookbackDays = 90;

    private readonly IReportRepository _reportRepository;

    public InsightGeneratorJob(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public Task RunAsync(CancellationToken ct = default)
        => GenerateAsync(VietnamTime.Today(), ct);

    public async Task GenerateAsync(DateOnly today, CancellationToken ct = default)
    {
        var lookbackStart = today.AddDays(-ActivityLookbackDays);
        var userIds = await _reportRepository.GetUserIdsWithRecentActivityAsync(lookbackStart, today, ct);

        foreach (var userId in userIds)
        {
            // Từ 2026-11-02 chỉ còn insight so với tháng trước: khoản định kỳ chuyển sang recurring_items (Phase 54),
            // chi tiêu bất thường chuyển sang transaction_anomalies có phản hồi (Phase 56).
            await GenerateVsLastMonthAsync(userId, today, ct);
        }
    }

    private async Task GenerateVsLastMonthAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var monthFirst = new DateOnly(today.Year, today.Month, 1);
        var prevFirst = monthFirst.AddMonths(-1);

        // So CÙNG KỲ: tháng trước cũng chỉ lấy tới ngày thứ N. So cả tháng trước với nửa
        // tháng này thì lúc nào cũng ra "bạn tiêu ít hơn", vô nghĩa.
        var daysElapsed = today.Day;
        var prevSameDay = prevFirst.AddDays(Math.Min(daysElapsed, DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month)) - 1);

        if (await _reportRepository.InsightExistsAsync(userId, InsightType.VsLastMonth, null, monthFirst, ct))
        {
            return;
        }

        var thisMonth = await _reportRepository.GetDailySpendAsync(userId, monthFirst, today, ct);
        var lastMonth = await _reportRepository.GetDailySpendAsync(userId, prevFirst, prevSameDay, ct);

        var thisSpent = thisMonth.Sum(p => p.SpentCents);
        var lastSpent = lastMonth.Sum(p => p.SpentCents);

        if (lastSpent <= 0)
        {
            return;
        }

        var changePercent = (thisSpent - lastSpent) * 100.0 / lastSpent;
        if (Math.Abs(changePercent) < VsLastMonthThresholdPercent)
        {
            return;
        }

        var rounded = Math.Abs(Math.Round(changePercent));
        var title = changePercent > 0 ? "Tháng này bạn đang tiêu nhiều hơn" : "Tháng này bạn đang tiêu ít hơn";
        var direction = changePercent > 0 ? "nhiều hơn" : "ít hơn";

        await AddAsync(new SpendingInsight
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InsightType = InsightType.VsLastMonth,
            Title = title,
            Body = $"Tính tới ngày {daysElapsed}, bạn đã chi {rounded:0}% {direction} so với cùng kỳ tháng trước.",
            AmountCents = Math.Abs(thisSpent - lastSpent),
            PeriodStart = monthFirst,
            PeriodEnd = today,
        }, ct);
    }


    private Task AddAsync(SpendingInsight insight, CancellationToken ct)
    {
        insight.CreatedAt = DateTimeOffset.UtcNow;
        return _reportRepository.AddInsightAsync(insight, ct);
    }
}
