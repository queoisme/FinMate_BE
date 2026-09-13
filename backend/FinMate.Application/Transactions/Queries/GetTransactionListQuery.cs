using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Queries;

/// <param name="Status">
/// Tab "Chờ duyệt (Pending)" của docx Bước 5.4 chính là <c>Status = Draft</c>: giao dịch AI
/// đã dựng nhưng người dùng chưa xác nhận. Không có filter này thì client không có cách nào
/// tách chúng ra khỏi lịch sử.
/// </param>
public record GetTransactionListQuery(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CategoryId,
    TransactionType? TransactionType,
    TransactionStatus? Status,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit);
