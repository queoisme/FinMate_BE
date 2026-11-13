using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Tổng chi/thu của một ngày (giờ VN) — dùng cho DailySummaryJob và forecast. Phase 81 (<see cref="Reports.NetFlow"/>): <c>SpentCents</c> là chi
/// RÒNG (chi − hoàn, có thể âm ở một ngày chỉ có hoàn; caller chặn ở 0 khi cộng thành tổng kỳ), <c>IncomeCents</c> không gồm hoàn,
/// <c>RefundedCents</c> là tổng hoàn trong ngày; cặp đảo bị loại khỏi cả ba và khỏi số lượng.
/// </summary>
public record DailySpendPoint(DateOnly Date, long SpentCents, long IncomeCents, int TransactionCount, long RefundedCents = 0);

/// <summary>Chi tiêu RÒNG (chi − hoàn) gom theo category trong một khoảng; CategoryId null = chưa phân loại. Danh mục ròng ≤ 0 không có dòng (D3).</summary>
public record CategorySpend(Guid? CategoryId, string? CategoryName, string? CategorySlug, long SpentCents);

/// <summary>Một merchant lặp lại đều đặn — ứng viên của insight recurring_detected.</summary>
public record RecurringCandidate(string MerchantName, long MedianAmountCents, int Occurrences);

public interface IReportRepository
{
    Task<DailySummary?> GetDailySummaryAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task<List<DailySummary>> GetDailySummariesAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task UpsertDailySummaryAsync(DailySummary summary, CancellationToken ct = default);

    /// <summary>Tổng hợp live từ transactions cho một khoảng ngày (giờ VN).</summary>
    Task<List<DailySpendPoint>> GetDailySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<List<CategorySpend>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Phase 81, D3: phần hoàn nhận trong khoảng mà khoản chi gốc nằm NGOÀI khoảng (mua tháng trước, hoàn tháng này). Đây là phần làm tổng chi
    /// kỳ thấp hơn tổng các dòng danh mục; app dùng nó để giải thích chỗ chênh. Hoàn mồ côi không có khoản gốc nên không tính.
    /// </summary>
    Task<long> GetCrossPeriodRefundsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>User có giao dịch trong ngày — DailySummaryJob chỉ chạm những user này.</summary>
    Task<List<Guid>> GetUserIdsWithActivityAsync(DateOnly date, CancellationToken ct = default);

    Task<List<Guid>> GetUserIdsWithRecentActivityAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<List<SpendingInsight>> GetInsightsAsync(Guid userId, bool unreadOnly, int limit, CancellationToken ct = default);
    Task<SpendingInsight?> GetInsightAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<bool> InsightExistsAsync(Guid userId, Domain.Enums.InsightType type, Guid? categoryId, DateOnly periodStart, CancellationToken ct = default);
    Task AddInsightAsync(SpendingInsight insight, CancellationToken ct = default);
    Task UpdateInsightAsync(SpendingInsight insight, CancellationToken ct = default);

    /// <summary>Giao dịch chi tiêu đã Confirmed trong khoảng — dùng cho phát hiện recurring/bất thường. Bỏ khoản đã đảo và khoản đã được hoàn 100%.</summary>
    Task<List<Transaction>> GetConfirmedDebitsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
