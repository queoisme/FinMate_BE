using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public record TransactionListFilter(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CategoryId,
    TransactionType? TransactionType,
    TransactionStatus? Status,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    string? Cursor,
    int Limit,
    bool ExcludeLoans = false);  // cho vay/mượn không phải chi tiêu hay thu nhập (cùng quy ước với báo cáo): danh sách khoản của chat phải khớp với tổng

public record TransactionListResult(IReadOnlyList<Transaction> Items, string? NextCursor);

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Giao dịch đã tạo từ chính lần gửi này, hoặc null. Lọc theo <paramref name="userId"/>
    /// như mọi truy vấn khác — id do client sinh nên không thể tin nó là duy nhất toàn hệ thống.
    /// </summary>
    Task<Transaction?> GetByClientRequestIdAsync(
        Guid userId, Guid clientRequestId, CancellationToken ct = default);

    /// <summary>
    /// Giao dịch nháp đã tạo từ một thông báo ngân hàng cụ thể, hoặc null nếu thông báo đó
    /// không sinh ra giao dịch nào (không phải thông báo tài chính, hoặc nghi trùng).
    /// notification_log_id là duy nhất nên không cần lọc theo userId.
    /// </summary>
    Task<Transaction?> GetByNotificationLogIdAsync(Guid notificationLogId, CancellationToken ct = default);
    Task<TransactionListResult> GetListAsync(TransactionListFilter filter, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default);
    Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Tổng chi tiêu (chỉ Debit, chỉ Confirmed) trong nửa mở [from, to). <paramref name="categoryId"/>
    /// null = mọi category. Dùng để backfill budget_periods.spent_cents khi user tạo budget giữa
    /// chu kỳ — không có nó, budget mới luôn hiện 0 dù user đã tiêu trong tháng.
    /// </summary>
    Task<long> SumConfirmedSpendAsync(
        Guid userId,
        Guid? categoryId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>
    /// Khoản chi (Debit, không phải chuyển tiền) cùng số tiền của chính <paramref name="userId"/> trong khoảng
    /// [<paramref name="at"/> − window, <paramref name="at"/> + window], hoặc null. Chỉ để CẢNH BÁO có thể trùng; không bao giờ chặn.
    /// <paramref name="exceptClientRequestId"/>: bỏ qua giao dịch của chính lần gửi này, để gửi lại cùng một yêu cầu vẫn nhận lại giao dịch cũ.
    /// </summary>
    Task<Guid?> FindPossibleDuplicateAsync(
        Guid userId,
        long amountCents,
        DateTimeOffset at,
        TimeSpan window,
        Guid? exceptClientRequestId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Quyết định trùng nghiệp vụ THẬT (§5.5) — khác <see cref="FindPossibleDuplicateAsync"/> (chỉ
    /// cảnh báo, không bao giờ chặn, chỉ xét Debit, lọc theo UserId). Lọc theo FinancialAccountId +
    /// AmountCents + TransactionType + TransactedAt trong 1 cửa sổ ngắn, xét cả Debit và Credit.
    /// </summary>
    Task<bool> HasAuthoritativeDuplicateAsync(
        Guid financialAccountId,
        long amountCents,
        TransactionType transactionType,
        DateTimeOffset at,
        TimeSpan window,
        Guid? excludeTransactionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Vế đối ứng của 1 lần chuyển tiền nội bộ cùng user (§16.3): cùng số tiền, chiều ngược,
    /// ví khác, trong cửa sổ, và CÒN đang Draft (v1 không merge vào giao dịch đã Confirmed).
    /// </summary>
    Task<Transaction?> FindMergeCandidateAsync(
        Guid userId,
        long amountCents,
        TransactionType oppositeType,
        Guid excludeFinancialAccountId,
        DateTimeOffset at,
        TimeSpan window,
        CancellationToken ct = default);
}
