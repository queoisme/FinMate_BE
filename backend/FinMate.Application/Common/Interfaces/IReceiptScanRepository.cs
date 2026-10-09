using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IReceiptScanRepository
{
    Task AddAsync(ReceiptScan scan, CancellationToken ct = default);

    /// <summary>Lưu các món đã bóc tách của một lần quét (gán <c>ScanId</c> cho từng món).</summary>
    Task AddItemsAsync(Guid scanId, IReadOnlyList<ReceiptScanItem> items, CancellationToken ct = default);

    /// <summary>
    /// Nối một nhóm (<paramref name="groupCategorySlug"/>, <c>"unknown"</c> khi nhóm không có slug) của lần quét với giao dịch đã lưu từ nhóm đó.
    /// True khi tạo được dòng nối; false (không bao giờ ném) khi lần quét không tồn tại hoặc của người khác, nhóm đó đã được nối, hoặc giao dịch
    /// đã được nối (lần nối đầu thắng). Một câu INSERT ... ON CONFLICT DO NOTHING nên hai request cùng lúc không nối đôi.
    /// </summary>
    Task<bool> LinkToTransactionAsync(Guid scanId, Guid userId, Guid transactionId, string groupCategorySlug, CancellationToken ct = default);

    /// <summary>Lọc theo <paramref name="userId"/> như mọi truy vấn khác: id một lần quét không được đủ để đọc bản ghi của người khác.</summary>
    Task<ReceiptScan?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
}
