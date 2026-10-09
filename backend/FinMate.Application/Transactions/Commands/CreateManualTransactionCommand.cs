using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <param name="ClientRequestId">
/// Id do client sinh để chống tạo trùng khi gửi lại — xem <c>Transaction.ClientRequestId</c>.
/// Bỏ trống vẫn tạo bình thường, chỉ là không có bảo vệ.
/// </param>
/// <param name="ReceiptScanId">
/// Mã bản ghi quét (<c>scanId</c> mà <c>POST /transactions/scan-receipt</c> trả về) khi giao dịch được điền từ ảnh hóa đơn. Chỉ có tác dụng với
/// <c>Source = Receipt</c>. Mã sai, của người khác hoặc đã được nối thì bị BỎ QUA chứ không làm hỏng việc lưu: đây là siêu dữ liệu, còn
/// giao dịch là tiền của người dùng.
/// </param>
/// <param name="ReceiptGroup">
/// Slug nhóm (hoặc <c>"unknown"</c>) mà giao dịch này lưu thay cho khi hóa đơn được tách. Chỉ dùng làm khóa nối với lần quét: rỗng, toàn khoảng trắng
/// hoặc dài quá 100 ký tự thì quay về slug danh mục của giao dịch; không được tin hay dùng vào việc nào khác.
/// </param>
/// <param name="Source">
/// Kênh người dùng dùng để nhập: <c>Manual</c> (form/NLP), <c>Voice</c>, hay <c>Receipt</c>.
/// <c>Notification</c> bị TỪ CHỐI ở validator — xem lý do ở đó.
/// </param>
public record CreateManualTransactionCommand(
    Guid UserId,
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description,
    Guid? ClientRequestId = null,
    TransactionSource Source = TransactionSource.Manual,
    Guid? ReceiptScanId = null,
    string? ReceiptGroup = null);
