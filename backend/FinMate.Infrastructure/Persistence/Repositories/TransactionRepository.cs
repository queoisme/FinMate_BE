using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly FinMateDbContext _context;

    public TransactionRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.FinancialAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

    public async Task AddAsync(Transaction transaction, CancellationToken ct = default)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken ct = default)
    {
        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync(ct);
    }

    public Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default)
        => _context.Transactions.AnyAsync(t => t.FinancialAccountId == financialAccountId, ct);

    public Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default)
        => _context.Transactions.AnyAsync(t => t.CategoryId == categoryId, ct);
}
