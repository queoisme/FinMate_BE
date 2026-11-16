using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Chuyển khoản vượt ranh giới tập ví được tính vào dự báo (Phase 83, BR-TRF-11, S4): có đúng một ví thuộc tập đó, nên là tiền RA khỏi hoặc VÀO
/// tập đó. Không bao giờ vào thói quen chi, thu nhập, bất thường hay dự báo danh mục: chỉ dùng cho số thực tế của độ chính xác dự báo
/// (<see cref="ForecastActuals"/>) và để khớp khoản định kỳ.
/// </summary>
public static class BoundaryTransfers
{
    /// <summary>Mọi chiều, cho <see cref="ForecastActuals"/>. Repository chưa có dữ liệu (mock cũ) → rỗng.</summary>
    public static async Task<IReadOnlyList<ForecastTransaction>> AllAsync(
        IForecastDataRepository data, Guid userId, IReadOnlySet<Guid> countedAccountIds, DateOnly from, DateOnly to, CancellationToken ct)
        => (await data.GetBoundaryTransfersAsync(userId, countedAccountIds, from, to, ct)) ?? [];

    /// <summary>
    /// Chỉ tiền RA khỏi tập được tính, cho bộ khoản định kỳ: nạp tiết kiệm hằng tháng là khoản chi đều đặn; còn tiền vào từ ví bị loại không phải
    /// thu nhập, đưa nó vào bộ định kỳ thì nó sẽ thành "thu nhập định kỳ" trong dự báo thu nhập.
    /// </summary>
    public static async Task<IReadOnlyList<ForecastTransaction>> OutflowsAsync(
        IForecastDataRepository data, Guid userId, IReadOnlySet<Guid> countedAccountIds, DateOnly from, DateOnly to, CancellationToken ct)
        => (await AllAsync(data, userId, countedAccountIds, from, to, ct)).Where(t => t.Type == TransactionType.Debit).ToList();

    /// <summary>Lịch sử để KHỚP khoản định kỳ: thói quen + tiền ra vượt ranh giới.</summary>
    public static IReadOnlyList<ForecastTransaction> ForRecurring(IReadOnlyList<ForecastTransaction> habits, IReadOnlyList<ForecastTransaction> outflows)
        => outflows.Count == 0 ? habits : habits.Concat(outflows).ToList();
}
