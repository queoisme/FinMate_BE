using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IReceiptScanRepository
{
    Task AddAsync(ReceiptScan scan, CancellationToken ct = default);

    /// <summary>
    /// Nối lần quét với giao dịch đã lưu từ nó. True khi nối được; false khi không có lần quét nào của người dùng này với mã đó, hoặc nó đã được nối
    /// (lần nối đầu thắng, không ghi đè). Chạy một câu UPDATE có điều kiện nên hai request cùng lúc không nối đôi.
    /// </summary>
    Task<bool> LinkToTransactionAsync(Guid scanId, Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Lọc theo <paramref name="userId"/> như mọi truy vấn khác: id một lần quét không được đủ để đọc bản ghi của người khác.</summary>
    Task<ReceiptScan?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
}
