namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Chuyển tiền giữa 2 ví của cùng user (rút ATM, nạp ví điện tử).
/// Xem ARCHITECTURE.md §0 quyết định #1 — transfer không tiêu ngân sách và không vào báo cáo.
/// </summary>
public record CreateTransferCommand(
    Guid UserId,
    Guid FromAccountId,
    Guid ToAccountId,
    long AmountCents,
    DateTimeOffset TransactedAt,
    string? Description,
    Guid? ClientRequestId = null);
