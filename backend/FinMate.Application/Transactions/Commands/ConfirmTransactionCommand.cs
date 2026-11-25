namespace FinMate.Application.Transactions.Commands;

/// <param name="CategoryId">
/// Danh mục người dùng chọn khi xác nhận, hoặc null để giữ nguyên danh mục AI đoán.
///
/// Có mặt để docx Flow 1 bước 5.3 thật sự là "1 chạm": không có nó, đổi danh mục phải gọi
/// <c>PUT /transactions/{id}</c> rồi mới <c>confirm</c> — hai lần gọi mạng cho một thao tác,
/// và có khoảng giữa hai lần đó mà trạng thái lỡ dở.
/// </param>
/// <param name="RelatedTransactionId">
/// Phase 81: xác nhận nháp này là khoản hoàn/đảo của khoản chi đó (người dùng chọn từ <c>suggestedRelatedTransactions</c> hoặc tự chọn).
/// Không gửi = giữ liên kết đã đặt bằng <c>PUT /related</c> (nếu có), không thì là thu nhập thường như trước.
/// </param>
/// <param name="Relation">Mặc định <c>Refund</c>.</param>
/// <param name="LoanId">
/// Phase 90 (D4): xác nhận nháp này là một dòng của khoản vay đó, trong cùng lượt lưu: danh mục thành <c>loan</c>, loại dòng suy từ chiều khoản và
/// loại giao dịch. Trả vượt gốc còn lại trong ngưỡng lãi thì phần lãi thành giao dịch riêng (S1). Không đi cùng hoàn/đảo hay chia sẻ nhóm (422).
/// </param>
public record ConfirmTransactionCommand(
    Guid UserId,
    Guid TransactionId,
    Guid? CategoryId = null,
    Guid? GroupId = null,
    Guid? GroupCategoryId = null,
    Guid? RelatedTransactionId = null,
    Domain.Enums.TransactionRelation? Relation = null,
    Guid? LoanId = null);
