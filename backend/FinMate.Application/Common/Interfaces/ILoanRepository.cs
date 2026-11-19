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

    /// <summary>Khoản còn nợ gốc, có hạn cuối &lt;= <paramref name="dueOnOrBefore"/> và chưa nhắc.</summary>
    Task<List<Loan>> GetDueUnnotifiedAsync(DateOnly dueOnOrBefore, CancellationToken ct = default);
}
