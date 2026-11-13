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
    bool ExcludeLoans = false,
    TransactionRelation? Relation = null);  // Phase 81: chỉ khoản hoàn hoặc chỉ khoản đảo  // cho vay/mượn không phải chi tiêu hay thu nhập (cùng quy ước với báo cáo): danh sách khoản của chat phải khớp với tổng

public record TransactionListResult(IReadOnlyList<Transaction> Items, string? NextCursor);

/// <summary>Một khoản chi có thể là gốc của khoản hoàn đang xét (Phase 81). Chỉ để GỢI Ý; người dùng mới là người liên kết.</summary>
public sealed record RefundCandidate(Transaction Original, long RefundedCents, long RemainingRefundableCents);

/// <summary>Điều kiện tìm gốc cho một khoản hoàn. <paramref name="WindowDays"/> chỉ giới hạn GỢI Ý, không giới hạn liên kết tay (D12).</summary>
public sealed record RefundCandidateQuery(
    Guid UserId,
    long AmountCents,
    DateTimeOffset At,
    int WindowDays,
    Guid? FinancialAccountId,
    Guid? ProviderConfigId,
    string? CounterpartyKey,
    int Limit,
    Guid? ExcludeTransactionId = null);

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
    /// Vẫn lọc theo <paramref name="userId"/> dù notification_log_id là duy nhất: không phương thức repository nào
    /// được lấy dữ liệu của người dùng chỉ bằng id (`CLAUDE.md`).
    /// </summary>
    Task<Transaction?> GetByNotificationLogIdAsync(Guid userId, Guid notificationLogId, CancellationToken ct = default);
    Task<TransactionListResult> GetListAsync(TransactionListFilter filter, CancellationToken ct = default);
    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default);
    Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>
    /// Chi ròng đã xác nhận (chi − hoàn, bỏ cặp đảo, chặn dưới ở 0) trong nửa mở [from, to), cùng định nghĩa với
    /// <see cref="Budgets.BudgetSpendScope"/>. <paramref name="categoryId"/> null = mọi category. Dùng để backfill
    /// budget_periods.spent_cents khi user tạo budget giữa chu kỳ — không có nó, budget mới luôn hiện 0 dù user đã tiêu trong tháng.
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
    /// <summary>Tổng các khoản hoàn/đảo ĐÃ XÁC NHẬN, chưa xoá, trỏ tới <paramref name="originalId"/> (cùng user). <paramref name="excludeTransactionId"/>: bỏ chính khoản đang sửa.</summary>
    Task<long> SumConfirmedRefundsAsync(Guid userId, Guid originalId, Guid? excludeTransactionId = null, CancellationToken ct = default);

    /// <summary>Như trên cho cả một trang danh sách, một truy vấn. Khoản gốc không có khoản hoàn nào thì không có trong kết quả.</summary>
    Task<IReadOnlyDictionary<Guid, long>> SumConfirmedRefundsAsync(Guid userId, IReadOnlyCollection<Guid> originalIds, CancellationToken ct = default);

    /// <summary>Các khoản hoàn/đảo (kể cả nháp) đang trỏ tới khoản gốc, đã nạp danh mục. Để danh mục và nhóm đi theo khoản gốc (BR-RF-10/11).</summary>
    Task<IReadOnlyList<Transaction>> ListRelatedAsync(Guid userId, Guid originalId, CancellationToken ct = default);

    /// <summary>
    /// Chạy <paramref name="work"/> trong một DB transaction tường minh, sau khi khoá dòng khoản gốc bằng <c>SELECT … FOR UPDATE</c>
    /// (D19). <paramref name="work"/> tự lưu; commit khi nó trả về. Không thấy khoản gốc của user này → <c>NotFoundException</c> (404).
    /// Thứ tự khoá CỐ ĐỊNH để hai luồng không chờ nhau vòng tròn: dòng khoản gốc → ví (xmin) → kỳ ngân sách (xmin).
    /// </summary>
    Task<T> RunWithOriginalLockedAsync<T>(Guid userId, Guid originalId, Func<Transaction, Task<T>> work, CancellationToken ct = default);

    /// <summary>Tối đa <see cref="RefundCandidateQuery.Limit"/> khoản chi còn hoàn được, xếp hạng: cùng người nhận &gt; cùng ví/nhà cung cấp &gt; đúng số tiền &gt; gần nhất.</summary>
    Task<IReadOnlyList<RefundCandidate>> FindRefundCandidatesAsync(RefundCandidateQuery query, CancellationToken ct = default);

    /// <summary>
    /// Chi ròng (chi − hoàn, bỏ cặp đảo) trong một phạm vi ngân sách theo <see cref="Budgets.BudgetSpendScope"/>, KHÔNG chặn ở 0, bỏ các
    /// giao dịch có id trong <paramref name="excludeTransactionIds"/> (chúng đang sửa trong bộ nhớ, caller tự cộng phần của chúng).
    /// </summary>
    Task<long> SumNetSpendAsync(System.Linq.Expressions.Expression<Func<Transaction, bool>> scope, IReadOnlyCollection<Guid> excludeTransactionIds, CancellationToken ct = default);

    Task<Guid?> FindPossibleDuplicateAsync(
        Guid userId,
        long amountCents,
        DateTimeOffset at,
        TimeSpan window,
        Guid? exceptClientRequestId = null,
        CancellationToken ct = default);
}
