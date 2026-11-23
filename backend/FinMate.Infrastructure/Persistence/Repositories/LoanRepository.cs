using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class LoanRepository(FinMateDbContext context) : ILoanRepository
{
    // Phase 90: khoản và dòng đã xoá mềm không bao giờ được đọc ở đây.
    public Task<List<Loan>> GetListAsync(Guid userId, CancellationToken ct = default)
        => context.Loans.Include(l => l.Repayments.Where(r => r.DeletedAt == null))
            .Where(l => l.UserId == userId && l.DeletedAt == null).OrderByDescending(l => l.CreatedAt).ToListAsync(ct);

    public Task<Loan?> GetOwnedAsync(Guid id, Guid userId, CancellationToken ct = default)
        => context.Loans.Include(l => l.Repayments.Where(r => r.DeletedAt == null))
            .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId && l.DeletedAt == null, ct);

    public async Task AddAsync(Loan loan, CancellationToken ct = default)
    {
        context.Loans.Add(loan);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Loan loan, CancellationToken ct = default) => await context.SaveChangesAsync(ct);

    public async Task DeleteAsync(Loan loan, CancellationToken ct = default)
    {
        context.Loans.Remove(loan);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddRepaymentAsync(Loan loan, LoanRepayment repayment, CancellationToken ct = default)
    {
        context.LoanRepayments.Add(repayment);
        loan.UpdatedAt = DateTimeOffset.UtcNow;   // buộc UPDATE dòng khoản vay để khóa xmin có tác dụng
        await context.SaveChangesAsync(ct);
    }

    public async Task SaveAsync(CancellationToken ct = default) => await context.SaveChangesAsync(ct);

    public async Task RemoveRepaymentAsync(Loan loan, LoanRepayment repayment, CancellationToken ct = default)
    {
        context.LoanRepayments.Remove(repayment);
        loan.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public Task<Loan?> GetByClientRequestAsync(Guid userId, Guid clientRequestId, CancellationToken ct = default)
        => context.Loans.Include(l => l.Repayments.Where(r => r.DeletedAt == null))
            .FirstOrDefaultAsync(l => l.UserId == userId && l.ClientRequestId == clientRequestId, ct);

    public Task<bool> EntryClientRequestExistsAsync(Guid loanId, Guid clientRequestId, CancellationToken ct = default)
        => context.LoanRepayments.AnyAsync(r => r.LoanId == loanId && r.ClientRequestId == clientRequestId, ct);

    public Task<bool> IsTransactionLinkedAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => context.LoanRepayments.AnyAsync(
            r => r.DeletedAt == null && (r.TransactionId == transactionId || r.InterestTransactionId == transactionId)
                 && context.Loans.Any(l => l.Id == r.LoanId && l.UserId == userId && l.DeletedAt == null),
            ct);

    public Task<Loan?> GetByLinkedTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => context.Loans.Include(l => l.Repayments.Where(r => r.DeletedAt == null))
            .FirstOrDefaultAsync(
                l => l.UserId == userId && l.DeletedAt == null && l.Repayments.Any(r => r.DeletedAt == null && r.TransactionId == transactionId), ct);

    public async Task<long> GetSplitInterestCentsAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => await context.LoanRepayments
            .Where(r => r.TransactionId == transactionId && r.DeletedAt == null && r.InterestTransactionId != null
                        && context.Loans.Any(l => l.Id == r.LoanId && l.UserId == userId && l.DeletedAt == null))
            .Select(r => context.Transactions.Where(t => t.Id == r.InterestTransactionId && t.DeletedAt == null).Select(t => (long?)t.AmountCents).FirstOrDefault())
            .FirstOrDefaultAsync(ct) ?? 0;

    public void Track(Loan loan) => context.Loans.Add(loan);

    public void Track(Transaction transaction) => context.Transactions.Add(transaction);

    public void Track(LoanRepayment entry) => context.LoanRepayments.Add(entry);

    public Task<List<Loan>> GetDueUnnotifiedAsync(DateOnly dueOnOrBefore, CancellationToken ct = default)
        => context.Loans
            .Where(l => l.DueDate != null && l.DueDate <= dueOnOrBefore && l.DueNotifiedAt == null
                        && l.DeletedAt == null && l.Status == LoanStatus.Open && l.RepaidCents < l.PrincipalCents)
            .ToListAsync(ct);
}
