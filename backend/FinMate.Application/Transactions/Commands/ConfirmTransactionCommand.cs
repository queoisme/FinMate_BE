namespace FinMate.Application.Transactions.Commands;

/// <param name="CategoryId">
/// Danh mục người dùng chọn khi xác nhận, hoặc null để giữ nguyên danh mục AI đoán.
///
/// Có mặt để docx Flow 1 bước 5.3 thật sự là "1 chạm": không có nó, đổi danh mục phải gọi
/// <c>PUT /transactions/{id}</c> rồi mới <c>confirm</c> — hai lần gọi mạng cho một thao tác,
/// và có khoảng giữa hai lần đó mà trạng thái lỡ dở.
/// </param>
public record ConfirmTransactionCommand(Guid UserId, Guid TransactionId, Guid? CategoryId = null);
