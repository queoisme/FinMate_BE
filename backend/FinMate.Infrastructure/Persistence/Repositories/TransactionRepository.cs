using System.Text;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
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

    public async Task<TransactionListResult> GetListAsync(TransactionListFilter filter, CancellationToken ct = default)
    {
        var query = _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == filter.UserId);

        if (filter.FinancialAccountId is not null)
        {
            query = query.Where(t => t.FinancialAccountId == filter.FinancialAccountId);
        }

        if (filter.CategoryId is not null)
        {
            query = query.Where(t => t.CategoryId == filter.CategoryId);
        }

        if (filter.TransactionType is not null)
        {
            query = query.Where(t => t.TransactionType == filter.TransactionType);
        }

        if (filter.FromDate is not null)
        {
            query = query.Where(t => t.TransactedAt >= filter.FromDate);
        }

        if (filter.ToDate is not null)
        {
            query = query.Where(t => t.TransactedAt <= filter.ToDate);
        }

        if (filter.Cursor is not null && TryDecodeCursor(filter.Cursor, out var cursorAt, out var cursorCreatedAt))
        {
            query = query.Where(t =>
                t.TransactedAt < cursorAt || (t.TransactedAt == cursorAt && t.CreatedAt < cursorCreatedAt));
        }

        var items = await query
            .OrderByDescending(t => t.TransactedAt)
            .ThenByDescending(t => t.CreatedAt)
            .Take(filter.Limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > filter.Limit)
        {
            var last = items[filter.Limit - 1];
            nextCursor = EncodeCursor(last.TransactedAt, last.CreatedAt);
            items = items.Take(filter.Limit).ToList();
        }

        return new TransactionListResult(items, nextCursor);
    }

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

    // Phải xét cả counter_account_id: ví chỉ từng đứng ở vế ĐÍCH của một transfer vẫn là ví
    // đang có giao dịch. Bỏ sót vế này thì guard xóa ví lọt, và FK Restrict sẽ ném lỗi DB thô.
    public Task<bool> HasAnyForAccountAsync(Guid financialAccountId, CancellationToken ct = default)
        => _context.Transactions.AnyAsync(
            t => t.FinancialAccountId == financialAccountId || t.CounterAccountId == financialAccountId,
            ct);

    public Task<bool> HasAnyForCategoryAsync(Guid categoryId, CancellationToken ct = default)
        => _context.Transactions.AnyAsync(t => t.CategoryId == categoryId, ct);

    public async Task<long> SumConfirmedSpendAsync(
        Guid userId,
        Guid? categoryId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var query = _context.Transactions.Where(t =>
            t.UserId == userId
            && t.Status == TransactionStatus.Confirmed
            && t.TransactionType == TransactionType.Debit
            && t.TransactedAt >= from
            && t.TransactedAt < to);

        if (categoryId is not null)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        return await query.SumAsync(t => (long?)t.AmountCents, ct) ?? 0;
    }

    private static string EncodeCursor(DateTimeOffset transactedAt, DateTimeOffset createdAt)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{transactedAt:O}|{createdAt:O}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset transactedAt, out DateTimeOffset createdAt)
    {
        transactedAt = default;
        createdAt = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2)
            {
                return false;
            }

            transactedAt = DateTimeOffset.Parse(parts[0]);
            createdAt = DateTimeOffset.Parse(parts[1]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
