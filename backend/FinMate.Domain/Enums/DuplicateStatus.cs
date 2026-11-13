namespace FinMate.Domain.Enums;

/// <summary>
/// Trạng thái nghi trùng của một giao dịch so với <c>Transaction.DuplicateOfTransactionId</c> (Phase 82, FR-TXN-17).
/// Bản gộp xong không mang trạng thái này: nó bị xoá mềm và trỏ tới survivor qua <c>MergedIntoTransactionId</c>.
/// </summary>
public enum DuplicateStatus
{
    /// <summary>Backend thấy một giao dịch khác có thể là cùng một khoản; chờ user gộp hoặc bác.</summary>
    Suspected,

    /// <summary>User xác nhận không trùng. Giữ liên kết để không gợi ý lại và để phát hiện bất thường không gắn cờ cặp này.</summary>
    Dismissed,
}
