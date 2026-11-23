using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface ILoanRepository
{
    /// <summary>Khoản vay của người dùng kèm các lần trả, mới nhất trước.</summary>
    Task<List<Loan>> GetListAsync(Guid userId, CancellationToken ct = default);
    Task<Loan?> GetOwnedAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task AddAsync(Loan loan, CancellationToken ct = default);
    Task UpdateAsync(Loan loan, CancellationToken ct = default);
    Task DeleteAsync(Loan loan, CancellationToken ct = default);

    /// <summary>Thêm lần trả và đồng thời cập nhật dòng khoản vay (khóa lạc quan): hai lần trả đồng thời thì một bên nhận xung đột.</summary>
    Task AddRepaymentAsync(Loan loan, LoanRepayment repayment, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task RemoveRepaymentAsync(Loan loan, LoanRepayment repayment, CancellationToken ct = default);

    /// <summary>Phase 90, D19: khoản đã tạo bằng cùng <c>clientRequestId</c> (kể cả đã xoá mềm, để gửi lại không tạo lại khoản vừa xoá), kèm dòng còn sống.</summary>
    Task<Loan?> GetByClientRequestAsync(Guid userId, Guid clientRequestId, CancellationToken ct = default);

    /// <summary>Phase 90, D19: dòng đã ghi bằng cùng <c>clientRequestId</c> trên khoản này, kể cả dòng đã xoá (UNIQUE gồm cả chúng).</summary>
    Task<bool> EntryClientRequestExistsAsync(Guid loanId, Guid clientRequestId, CancellationToken ct = default);

    /// <summary>Phase 90, BR-DEBT-08: giao dịch đã gắn một dòng còn sống (gốc hoặc lãi) của một khoản còn sống chưa.</summary>
    Task<bool> IsTransactionLinkedAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Phase 90: khoản (đang theo dõi, kèm dòng còn sống) có dòng còn sống gắn giao dịch này làm phần gốc; null khi chưa gắn.</summary>
    Task<Loan?> GetByLinkedTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Phase 90: liên kết còn sống của các giao dịch này (một truy vấn cho cả trang). Giao dịch lãi tách ra (S1) có <c>kind = interest</c>.</summary>
    Task<IReadOnlyDictionary<Guid, Loans.LoanLinkDto>> GetLinksAsync(Guid userId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>Phase 90: mọi khoản <c>open</c> còn sống kèm dòng còn sống, không theo dõi: gợi ý, dự báo, nhắc hạn.</summary>
    Task<IReadOnlyList<Loan>> ListOpenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Phase 90 (Q14): số tiền phần lãi đã tách khỏi giao dịch này (S1), 0 khi không tách hoặc giao dịch lãi đã xoá.</summary>
    Task<long> GetSplitInterestCentsAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Phase 90: chỉ theo dõi khoản mới; lưu cùng các thay đổi khác của lượt ghi (một lần lưu, D2).</summary>
    void Track(Loan loan);

    /// <summary>Phase 90: chỉ theo dõi giao dịch mới do sổ vay tạo (giao dịch <c>loan</c>, giao dịch lãi S1); lưu cùng lượt ghi.</summary>
    void Track(Transaction transaction);

    /// <summary>
    /// Phase 90: dòng mới của một khoản đang theo dõi. Phải thêm tường minh: id đặt sẵn + cột có giá trị mặc định làm EF tưởng dòng đã tồn tại
    /// nếu chỉ thêm vào danh sách của khoản (UPDATE 0 dòng → 409).
    /// </summary>
    void Track(LoanRepayment entry);

    /// <summary>Khoản còn nợ gốc, có hạn cuối &lt;= <paramref name="dueOnOrBefore"/> và chưa nhắc.</summary>
    Task<List<Loan>> GetDueUnnotifiedAsync(DateOnly dueOnOrBefore, CancellationToken ct = default);
}
