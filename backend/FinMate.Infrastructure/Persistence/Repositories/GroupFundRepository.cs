using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupFundRepository : IGroupFundRepository
{
    private readonly FinMateDbContext _context;

    public GroupFundRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<GroupFund>> ListAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupFunds
            .Where(f => f.GroupId == groupId)
            .OrderBy(f => f.ArchivedAt != null)
            .ThenBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public Task<GroupFund?> GetAsync(Guid groupId, Guid fundId, CancellationToken ct = default)
        => _context.GroupFunds.FirstOrDefaultAsync(f => f.Id == fundId && f.GroupId == groupId, ct);

    public Task<int> CountActiveAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupFunds.CountAsync(f => f.GroupId == groupId && f.ArchivedAt == null, ct);

    public void Add(GroupFund fund) => _context.GroupFunds.Add(fund);

    public async Task<GroupFundEntryPage> ListEntriesAsync(GroupFundEntryQuery query, CancellationToken ct = default)
    {
        var entries = _context.GroupFundEntries.AsNoTracking()
            .Where(e => e.FundId == query.FundId && e.DeletedAt == null)
            .Where(e => query.Kind == null || e.Kind == query.Kind)
            .Where(e => query.MemberUserId == null || e.MemberUserId == query.MemberUserId);

        if (KeysetCursor.TryDecode(query.Cursor, out var cursorAt, out var cursorId))
        {
            entries = entries.Where(e => e.TransactedAt < cursorAt || (e.TransactedAt == cursorAt && e.Id.CompareTo(cursorId) < 0));
        }

        var rows = await Views(entries.OrderByDescending(e => e.TransactedAt).ThenByDescending(e => e.Id).Take(query.Limit + 1)).ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > query.Limit)
        {
            var last = rows[query.Limit - 1].Entry;
            nextCursor = KeysetCursor.Encode(last.TransactedAt, last.Id);
            rows = rows.Take(query.Limit).ToList();
        }

        return new GroupFundEntryPage(rows, nextCursor);
    }

    public Task<GroupFundEntryView?> GetEntryViewAsync(Guid fundId, Guid entryId, CancellationToken ct = default)
        => Views(_context.GroupFundEntries.AsNoTracking().Where(e => e.Id == entryId && e.FundId == fundId && e.DeletedAt == null)).FirstOrDefaultAsync(ct);

    public Task<GroupFundEntry?> GetEntryAsync(Guid fundId, Guid entryId, CancellationToken ct = default)
        => _context.GroupFundEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.FundId == fundId, ct);

    public Task<GroupFundEntry?> GetEntryByIdAsync(Guid entryId, CancellationToken ct = default)
        => _context.GroupFundEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);

    public Task<GroupFundEntry?> GetEntryByClientRequestAsync(Guid fundId, Guid memberUserId, Guid clientRequestId, CancellationToken ct = default)
        => _context.GroupFundEntries.FirstOrDefaultAsync(
            e => e.FundId == fundId && e.MemberUserId == memberUserId && e.ClientRequestId == clientRequestId, ct);

    public Task<Transaction?> GetPersonalTransactionAsync(Guid entryId, Guid userId, bool includeDeleted = false, CancellationToken ct = default)
        => (includeDeleted ? _context.Transactions.IgnoreQueryFilters() : _context.Transactions)
            .FirstOrDefaultAsync(t => t.GroupFundEntryId == entryId && t.UserId == userId, ct);

    public Task<List<GroupFundExpenseRow>> ListExpenseRowsAsync(Guid groupId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => (from e in _context.GroupFundEntries.AsNoTracking()
            join u in _context.Users.IgnoreQueryFilters() on e.MemberUserId equals (Guid?)u.Id into members
            from u in members.DefaultIfEmpty()
            where e.GroupId == groupId && e.Kind == GroupFundEntryKind.Expense && e.DeletedAt == null && e.TransactedAt >= start && e.TransactedAt < end
            select new GroupFundExpenseRow(
                e.MemberUserId,
                u != null ? u.DisplayName : null,
                u != null && u.DeletedAt == null,
                e.AmountCents,
                e.GroupCategoryId,
                e.GroupCategory != null ? e.GroupCategory.Name : null,
                e.CategoryId,
                e.Category != null ? e.Category.Name : null))
            .ToListAsync(ct);

    public Task<List<GroupFundEntryView>> ListEntriesInRangeAsync(Guid fundId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => Views(_context.GroupFundEntries.AsNoTracking().Where(e => e.FundId == fundId && e.DeletedAt == null && e.TransactedAt >= start && e.TransactedAt < end))
            .ToListAsync(ct);

    // Cùng dấu với GroupFundMath.Signed: góp và điều chỉnh theo số của chính nó, chi và rút là trừ.
    public async Task<long> SumBeforeAsync(Guid fundId, DateTimeOffset before, CancellationToken ct = default)
        => await _context.GroupFundEntries
            .Where(e => e.FundId == fundId && e.DeletedAt == null && e.TransactedAt < before)
            .SumAsync(e => (long?)(e.Kind == GroupFundEntryKind.Contribution || e.Kind == GroupFundEntryKind.Adjustment ? e.AmountCents : -e.AmountCents), ct) ?? 0;

    public void AddEntry(GroupFundEntry entry) => _context.GroupFundEntries.Add(entry);

    public void AddPersonalTransaction(Transaction transaction) => _context.Transactions.Add(transaction);

    // IgnoreQueryFilters trên Users: người đang chờ xoá tài khoản vẫn hiện (dưới tên thay thế). member_user_id null là người đã bị ẩn danh.
    private IQueryable<GroupFundEntryView> Views(IQueryable<GroupFundEntry> entries)
        => from e in entries
           join u in _context.Users.IgnoreQueryFilters() on e.MemberUserId equals (Guid?)u.Id into members
           from u in members.DefaultIfEmpty()
           select new GroupFundEntryView(
               e,
               u != null ? u.DisplayName : null,
               u != null && u.DeletedAt == null,
               e.GroupCategory != null ? e.GroupCategory.Name : e.Category != null ? e.Category.Name : null,
               // DeletedAt tường minh: IgnoreQueryFilters ở trên áp cho CẢ câu truy vấn, nên bộ lọc xoá mềm của Transactions cũng tắt.
               _context.Transactions.Any(t => t.GroupFundEntryId == e.Id && t.DeletedAt == null));
}
