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
            await SummariseUserAsync(userId, date, ct);
        }
    }

    /// <summary>
    /// Chốt (hoặc chốt lại) một ngày của một user. Ngày không còn giao dịch nào được tính (ví dụ cặp đảo vừa được ghi, Phase 81) thì dòng
    /// đã có về 0 chứ không để số cũ; chưa có dòng thì không tạo dòng rỗng.
    /// </summary>
    public async Task SummariseUserAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var point = (await _reportRepository.GetDailySpendAsync(userId, date, date, ct)).FirstOrDefault();
        if (point is null && await _reportRepository.GetDailySummaryAsync(userId, date, ct) is null)
        {
            return;
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
            TotalSpentCents = point?.SpentCents ?? 0,
            TotalIncomeCents = point?.IncomeCents ?? 0,
            TotalRefundedCents = point?.RefundedCents ?? 0,
            TransactionCount = point?.TransactionCount ?? 0,
            TopCategoryId = top?.CategoryId,
            TopCategorySpentCents = top?.SpentCents ?? 0,
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);
    }
}
