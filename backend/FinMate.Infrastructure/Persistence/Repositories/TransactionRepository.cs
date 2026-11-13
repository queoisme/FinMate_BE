using System.Linq.Expressions;
using System.Text;
using FinMate.Application.Budgets;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
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
    public Task<Guid?> FindPossibleDuplicateAsync(
        Guid userId,
        long amountCents,
        DateTimeOffset at,
        TimeSpan window,
        Guid? exceptClientRequestId = null,
        CancellationToken ct = default)
    {
        var from = at - window;
        var to = at + window;
        return _context.Transactions
            .Where(t => t.UserId == userId
                && t.TransactionType == TransactionType.Debit
                && t.CounterAccountId == null
                && t.AmountCents == amountCents
                && t.TransactedAt >= from
                && t.TransactedAt <= to
                && (exceptClientRequestId == null || t.ClientRequestId != exceptClientRequestId))
            .OrderByDescending(t => t.TransactedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<Transaction?> GetByNotificationLogIdAsync(Guid userId, Guid notificationLogId, CancellationToken ct = default)
        => _context.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.NotificationLogId == notificationLogId, ct);

    public Task<Transaction?> GetByClientRequestIdAsync(
        Guid userId, Guid clientRequestId, CancellationToken ct = default)
        => _context.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(
                t => t.UserId == userId && t.ClientRequestId == clientRequestId, ct);


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

        if (filter.Status is not null)
        {
            query = query.Where(t => t.Status == filter.Status);
        }

        if (filter.Relation is not null)
        {
            query = query.Where(t => t.Relation == filter.Relation);
        }

        if (filter.ExcludeLoans)
        {
            query = query.Where(t => t.Category == null || t.Category.Slug != Category.LoanSlug);
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
        => Math.Max(0, await SumNetSpendAsync(BudgetSpendScope.Personal(userId, categoryId, from, to), [], ct));

    public async Task<long> SumNetSpendAsync(Expression<Func<Transaction, bool>> scope, IReadOnlyCollection<Guid> excludeTransactionIds, CancellationToken ct = default)
    {
        var query = _context.Transactions.Where(scope).Where(BudgetSpendScope.Counts);
        if (excludeTransactionIds.Count > 0)
        {
            query = query.Where(t => !excludeTransactionIds.Contains(t.Id));
        }

        return await query.Select(BudgetSpendScope.Signed).SumAsync(ct);
    }

    public Task<long> SumConfirmedRefundsAsync(Guid userId, Guid originalId, Guid? excludeTransactionId = null, CancellationToken ct = default)
        => _context.Transactions
            .Where(t => t.UserId == userId
                && t.RelatedTransactionId == originalId
                && t.Status == TransactionStatus.Confirmed
                && t.Relation != null
                && (excludeTransactionId == null || t.Id != excludeTransactionId))
            .SumAsync(t => t.AmountCents, ct);

    public async Task<IReadOnlyDictionary<Guid, long>> SumConfirmedRefundsAsync(Guid userId, IReadOnlyCollection<Guid> originalIds, CancellationToken ct = default)
    {
        if (originalIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        return await _context.Transactions
            .Where(t => t.UserId == userId
                && t.RelatedTransactionId != null
                && originalIds.Contains(t.RelatedTransactionId.Value)
                && t.Status == TransactionStatus.Confirmed
                && t.Relation != null)
            .GroupBy(t => t.RelatedTransactionId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.AmountCents) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, ct);
    }

    public async Task<IReadOnlyList<Transaction>> ListRelatedAsync(Guid userId, Guid originalId, CancellationToken ct = default)
        => await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && t.RelatedTransactionId == originalId)
            .ToListAsync(ct);

    public async Task<T> RunWithOriginalLockedAsync<T>(Guid userId, Guid originalId, Func<Transaction, Task<T>> work, CancellationToken ct = default)
    {
        // Thứ tự khoá CỐ ĐỊNH (D19): dòng khoản gốc (FOR UPDATE, ở đây) → ví (xmin, khi work lưu) → kỳ ngân sách (xmin, khi work lưu).
        // Mọi đường ghi một khoản hoàn/đảo đều vào đây trước tiên, nên hai khoản hoàn đồng thời của cùng một khoản gốc xếp hàng ở dòng
        // gốc: khoản sau chỉ đọc tổng đã hoàn SAU KHI khoản trước đã commit, và BR-RF-02 không thể bị vượt dù hai khoản vào hai ví khác nhau
        // (khoá xmin của ví chỉ bắt được trường hợp cùng ví). transactions không có cột xmin: thêm vào thì mọi lượt sửa giao dịch khác bắt đầu nhận 409.
        // Có transaction ngoài (đang chạy trong một transaction khác) thì dùng luôn nó: khoá vẫn giữ tới khi transaction ngoài kết thúc.
        var owns = _context.Database.CurrentTransaction is null;
        await using var transaction = owns ? await _context.Database.BeginTransactionAsync(ct) : null;

        // Lọc theo user NGAY trong câu khoá: id của người khác không khoá được gì và trả 404 như mọi chỗ khác.
        var locked = await _context.Transactions
            .FromSqlInterpolated($"SELECT * FROM transactions WHERE id = {originalId} AND user_id = {userId} AND deleted_at IS NULL FOR UPDATE")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (locked == Guid.Empty)
        {
            throw new NotFoundException("Transaction", originalId);
        }

        // Đọc lại khoản gốc SAU khi đã khoá (kèm danh mục), để số tiền, trạng thái và cờ đảo là bản mới nhất.
        var original = await _context.Transactions.Include(t => t.Category).FirstAsync(t => t.Id == originalId, ct);
        await _context.Entry(original).ReloadAsync(ct);

        var result = await work(original);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    public async Task<IReadOnlyList<RefundCandidate>> FindRefundCandidatesAsync(RefundCandidateQuery query, CancellationToken ct = default)
    {
        var from = query.At.AddDays(-query.WindowDays);
        var rows = await _context.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.FinancialAccount)
            .Where(t => t.UserId == query.UserId
                && t.TransactionType == TransactionType.Debit
                && t.Status == TransactionStatus.Confirmed
                && !t.IsReversed
                && (t.Category == null || t.Category.Slug != Category.LoanSlug)
                && t.AmountCents >= query.AmountCents
                && t.TransactedAt >= from
                && t.TransactedAt <= query.At
                && (query.ExcludeTransactionId == null || t.Id != query.ExcludeTransactionId))
            .Select(t => new
            {
                Original = t,
                Refunded = _context.Transactions
                    .Where(r => r.RelatedTransactionId == t.Id && r.Status == TransactionStatus.Confirmed && r.Relation != null)
                    .Sum(r => (long?)r.AmountCents) ?? 0,
            })
            .Where(x => x.Original.AmountCents - x.Refunded >= query.AmountCents)
            .ToListAsync(ct);

        // CounterpartyKey tính trong C# (bỏ dấu, gọn khoảng trắng) nên xếp hạng làm trong bộ nhớ; cửa sổ ngày và số tiền đã chặn số dòng.
        return rows
            .OrderByDescending(x => query.CounterpartyKey is not null && CounterpartyKey.From(x.Original.MerchantName) == query.CounterpartyKey)
            .ThenByDescending(x => SameWalletOrProvider(x.Original, query))
            .ThenByDescending(x => x.Original.AmountCents == query.AmountCents || x.Original.AmountCents - x.Refunded == query.AmountCents)
            .ThenBy(x => (query.At - x.Original.TransactedAt).Duration())
            .Take(query.Limit)
            .Select(x => new RefundCandidate(x.Original, x.Refunded, x.Original.AmountCents - x.Refunded))
            .ToList();
    }

    private static bool SameWalletOrProvider(Transaction original, RefundCandidateQuery query)
        => (query.FinancialAccountId is not null && original.FinancialAccountId == query.FinancialAccountId)
            || (query.ProviderConfigId is not null && original.FinancialAccount?.ProviderConfigId == query.ProviderConfigId);

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
