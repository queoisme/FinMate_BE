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

    private const int RecurringLookbackDays = 90;
    private const int RecurringMinOccurrences = 3;
    private const double RecurringAmountTolerance = 0.10;
    private const int RecurringMinIntervalDays = 25;
    private const int RecurringMaxIntervalDays = 35;

    private const int UnusualLookbackDays = 90;
    private const int UnusualMinSamples = 5;

    private readonly IReportRepository _reportRepository;

    public InsightGeneratorJob(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public Task RunAsync(CancellationToken ct = default)
        => GenerateAsync(VietnamTime.Today(), ct);

    public async Task GenerateAsync(DateOnly today, CancellationToken ct = default)
    {
        var lookbackStart = today.AddDays(-RecurringLookbackDays);
        var userIds = await _reportRepository.GetUserIdsWithRecentActivityAsync(lookbackStart, today, ct);

        foreach (var userId in userIds)
        {
            await GenerateVsLastMonthAsync(userId, today, ct);

            var transactions = await _reportRepository.GetConfirmedDebitsAsync(userId, lookbackStart, today, ct);
            await GenerateRecurringAsync(userId, today, transactions, ct);
            await GenerateUnusualSpendingAsync(userId, today, transactions, ct);
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

    private async Task GenerateRecurringAsync(
        Guid userId, DateOnly today, IReadOnlyList<Transaction> transactions, CancellationToken ct)
    {
        var periodStart = today.AddDays(-RecurringLookbackDays);

        var groups = transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.MerchantName))
            .GroupBy(t => t.MerchantName!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(t => t.TransactedAt).ToList();
            if (ordered.Count < RecurringMinOccurrences)
            {
                continue;
            }

            var median = (long)Statistics.Median(ordered.Select(t => (double)t.AmountCents).ToArray());
            if (median <= 0)
            {
                continue;
            }

            // Số tiền phải ổn định — cùng merchant nhưng mỗi lần một giá thì là mua sắm
            // thường ngày, không phải thuê bao.
            var amountsStable = ordered.All(t =>
                Math.Abs(t.AmountCents - median) <= median * RecurringAmountTolerance);
            if (!amountsStable)
            {
                continue;
            }

            var intervals = ordered
                .Zip(ordered.Skip(1), (a, b) => (VietnamTime.DateOf(b.TransactedAt).DayNumber
                                                 - VietnamTime.DateOf(a.TransactedAt).DayNumber))
                .ToList();

            var monthlyCadence = intervals.All(d => d is >= RecurringMinIntervalDays and <= RecurringMaxIntervalDays);
            if (!monthlyCadence)
            {
                continue;
            }

            var categoryId = ordered[^1].CategoryId;
            if (await _reportRepository.InsightExistsAsync(userId, InsightType.RecurringDetected, categoryId, periodStart, ct))
            {
                continue;
            }

            await AddAsync(new SpendingInsight
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InsightType = InsightType.RecurringDetected,
                Title = "Có vẻ đây là khoản chi định kỳ",
                Body = $"Bạn đã chi cho \"{group.Key}\" {ordered.Count} lần trong 3 tháng qua, "
                     + "mỗi lần cách nhau khoảng một tháng với số tiền gần như nhau.",
                CategoryId = categoryId,
                AmountCents = median,
                PeriodStart = periodStart,
                PeriodEnd = today,
            }, ct);
        }
    }

    private async Task GenerateUnusualSpendingAsync(
        Guid userId, DateOnly today, IReadOnlyList<Transaction> transactions, CancellationToken ct)
    {
        var periodStart = today.AddDays(-UnusualLookbackDays);

        foreach (var group in transactions.Where(t => t.CategoryId is not null).GroupBy(t => t.CategoryId!.Value))
        {
            var amounts = group.Select(t => (double)t.AmountCents).ToArray();
            if (amounts.Length < UnusualMinSamples)
            {
                continue;
            }

            var bound = Statistics.UpperOutlierBound(amounts);
            var outlier = group.Where(t => t.AmountCents > bound).MaxBy(t => t.AmountCents);
            if (outlier is null)
            {
                continue;
            }

            if (await _reportRepository.InsightExistsAsync(userId, InsightType.UnusualSpending, group.Key, periodStart, ct))
            {
                continue;
            }

            await AddAsync(new SpendingInsight
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InsightType = InsightType.UnusualSpending,
                Title = "Một khoản chi lớn bất thường",
                Body = $"Khoản chi cho \"{outlier.Category?.Name ?? "danh mục này"}\" ngày "
                     + $"{VietnamTime.DateOf(outlier.TransactedAt):dd/MM} cao hơn hẳn mức bạn thường chi cho nó.",
                CategoryId = group.Key,
                AmountCents = outlier.AmountCents,
                PeriodStart = periodStart,
                PeriodEnd = today,
            }, ct);
        }
    }

    private Task AddAsync(SpendingInsight insight, CancellationToken ct)
    {
        insight.CreatedAt = DateTimeOffset.UtcNow;
        return _reportRepository.AddInsightAsync(insight, ct);
    }
}
