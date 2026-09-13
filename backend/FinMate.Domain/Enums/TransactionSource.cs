namespace FinMate.Domain.Enums;

/// <summary>
/// Kênh mà giao dịch được ghi vào hệ thống.
///
/// Không chỉ để thống kê: <c>Notification</c> là điều kiện lọc của tỉ lệ "người dùng sửa lại
/// danh mục AI đoán" ở <c>/api/v1/admin/ai-stats</c>. Vì vậy client KHÔNG được phép tự khai
/// giá trị này — nếu được, một giao dịch tự nhập gắn nhãn Notification sẽ làm sai thước đo
/// chất lượng của chính AI. Xem guard ở <c>CreateManualTransactionCommandValidator</c>.
/// </summary>
public enum TransactionSource
{
    /// <summary>Do AI phát hiện từ thông báo ngân hàng — chỉ backend đặt được.</summary>
    Notification,

    /// <summary>Người dùng nhập tay qua form, hoặc gõ câu tự nhiên.</summary>
    Manual,

    /// <summary>Người dùng đọc bằng giọng nói; STT chạy ở client (docx phương thức 2).</summary>
    Voice,

    /// <summary>Người dùng chụp hóa đơn giấy (docx phương thức 3).</summary>
    Receipt,
}
