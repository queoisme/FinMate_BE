using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Chốt aggregate của ngày hôm qua (giờ VN) vào <c>daily_summaries</c> — ARCHITECTURE.md §5,
/// 00:05 mỗi ngày. Chỉ chạm user thực sự có giao dịch trong ngày đó.
/// </summary>
public class DailySummaryJob
{
    private readonly IReportRepository _reportRepository;

    public DailySummaryJob(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public Task RunAsync(CancellationToken ct = default)
        => SummariseAsync(VietnamTime.Today().AddDays(-1), ct);

    /// <summary>Tách riêng để test và để backfill một ngày cụ thể khi cần.</summary>
    public async Task SummariseAsync(DateOnly date, CancellationToken ct = default)
    {
        var userIds = await _reportRepository.GetUserIdsWithActivityAsync(date, ct);

        foreach (var userId in userIds)
        {
            var points = await _reportRepository.GetDailySpendAsync(userId, date, date, ct);
            var point = points.FirstOrDefault();
            if (point is null)
            {
                continue;
            }

            var categorySpend = await _reportRepository.GetCategorySpendAsync(userId, date, date, ct);
            var top = categorySpend
                .Where(c => c.CategoryId is not null)
                .MaxBy(c => c.SpentCents);

            var now = DateTimeOffset.UtcNow;
            await _reportRepository.UpsertDailySummaryAsync(new DailySummary
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SummaryDate = date,
                TotalSpentCents = point.SpentCents,
                TotalIncomeCents = point.IncomeCents,
                TransactionCount = point.TransactionCount,
                TopCategoryId = top?.CategoryId,
                TopCategorySpentCents = top?.SpentCents ?? 0,
                CreatedAt = now,
                UpdatedAt = now,
            }, ct);
        }
    }
}
