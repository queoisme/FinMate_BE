using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public sealed record TransactionAnomalyPage(IReadOnlyList<TransactionAnomaly> Items, string? NextCursor);

/// <summary>Đánh dấu chi tiêu bất thường. Mọi hàm lọc theo <c>userId</c>.</summary>
public interface IAnomalyRepository
{
    /// <summary>Đánh dấu (mọi trạng thái) của các giao dịch <paramref name="transactionIds"/>.</summary>
    Task<IReadOnlyList<TransactionAnomaly>> GetForTransactionsAsync(Guid userId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>Giao dịch đang bị đánh dấu mà user CHƯA xác nhận là bình thường — không dùng làm dữ liệu dự báo theo danh mục.</summary>
    Task<IReadOnlySet<Guid>> GetNotNormalTransactionIdsAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<PriorNormal>> GetPriorNormalsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Thêm và lưu. False nếu giao dịch đã có đánh dấu (hai lượt quét chạy song song).</summary>
    Task<bool> TryAddAsync(TransactionAnomaly anomaly, CancellationToken ct = default);

    /// <summary>Kèm giao dịch (tên cửa hàng, thời điểm). <paramref name="openOnly"/>: chỉ khoản chưa có phản hồi.</summary>
    Task<TransactionAnomalyPage> ListAsync(Guid userId, bool openOnly, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Có theo dõi thay đổi, kèm giao dịch.</summary>
    Task<TransactionAnomaly?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task UpdateAsync(TransactionAnomaly anomaly, CancellationToken ct = default);

    /// <summary>
    /// Phase 81 (R2): đóng các đánh dấu bất thường CÒN MỞ của một giao dịch với lý do hệ thống (<c>Refunded</c>/<c>Reversed</c>). Chỉ đổi dữ liệu
    /// đang được theo dõi, KHÔNG lưu: lưu cùng thao tác hoàn/đảo. Trả số dòng đã đóng.
    /// </summary>
    Task<int> ResolveOpenForTransactionAsync(Guid userId, Guid transactionId, Domain.Enums.AnomalyResolution resolution, DateTimeOffset at, CancellationToken ct = default);
}
