using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupBillRepository : IGroupBillRepository
{
    private readonly FinMateDbContext _context;

    public GroupBillRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public void AddBill(GroupBill bill) => _context.GroupBills.Add(bill);

    public void AddSettlement(GroupSettlement settlement) => _context.GroupSettlements.Add(settlement);

    public void AddReminder(GroupDebtReminder reminder) => _context.GroupDebtReminders.Add(reminder);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    // Hai truy vấn thay vì một phép chiếu: Include bị bỏ qua khi kết quả cuối không phải chính thực thể.
    // IgnoreQueryFilters trên Users: người đã yêu cầu xóa tài khoản vẫn đứng tên hóa đơn tới lúc xóa cứng.
    private async Task<List<GroupBillView>> ViewsAsync(IQueryable<GroupBill> query, CancellationToken ct)
    {
        var bills = await query.AsNoTracking().Include(b => b.Splits).Include(b => b.Category).Include(b => b.GroupCategory).ToListAsync(ct);
        var payerIds = bills.Where(b => b.PaidByUserId != null).Select(b => b.PaidByUserId!.Value).Distinct().ToList();
        var names = await _context.Users.IgnoreQueryFilters().Where(u => payerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return bills.Select(b => new GroupBillView(
            b,
            b.PaidByUserId is { } p && names.TryGetValue(p, out var n) ? n : null,
            b.Category?.Name,
            b.GroupCategory?.Name)).ToList();
    }

    public async Task<GroupBillView?> GetViewAsync(Guid groupId, Guid billId, CancellationToken ct = default)
        => (await ViewsAsync(_context.GroupBills.Where(b => b.GroupId == groupId && b.Id == billId), ct)).FirstOrDefault();

    public async Task<GroupBillPage> ListAsync(Guid groupId, Guid? onlyUserId, string? cursor, int limit, CancellationToken ct = default)
    {
        var query = _context.GroupBills.Where(b => b.GroupId == groupId);
        if (onlyUserId is { } uid)
        {
            query = query.Where(b => b.PaidByUserId == uid || b.Splits.Any(s => s.UserId == uid));
        }

        if (KeysetCursor.TryDecode(cursor, out var at, out var id))
        {
            query = query.Where(b => b.OccurredAt < at || (b.OccurredAt == at && b.Id.CompareTo(id) < 0));
        }

        var rows = await ViewsAsync(query.OrderByDescending(b => b.OccurredAt).ThenByDescending(b => b.Id).Take(limit + 1), ct);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToList();
        return new GroupBillPage(items, hasMore ? KeysetCursor.Encode(items[^1].Bill.OccurredAt, items[^1].Bill.Id) : null);
    }

    public async Task<GroupDebtRows> LoadDebtRowsAsync(Guid groupId, CancellationToken ct = default)
        => new(
            await _context.GroupBills.AsNoTracking().Include(b => b.Splits).Where(b => b.GroupId == groupId).ToListAsync(ct),
            await _context.GroupSettlements.AsNoTracking().Where(s => s.GroupId == groupId).ToListAsync(ct));

    public Task<List<GroupFundExpenseRow>> ListExpenseRowsAsync(Guid groupId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => (from b in _context.GroupBills.AsNoTracking()
            join u in _context.Users.IgnoreQueryFilters() on b.PaidByUserId equals (Guid?)u.Id into payers
            from u in payers.DefaultIfEmpty()
            where b.GroupId == groupId && b.OccurredAt >= start && b.OccurredAt < end
            select new GroupFundExpenseRow(
                b.PaidByUserId,
                u != null ? u.DisplayName : null,
                u != null && u.DeletedAt == null,
                b.AmountCents,
                b.GroupCategoryId,
                b.GroupCategory != null ? b.GroupCategory.Name : null,
                b.CategoryId,
                b.Category != null ? b.Category.Name : null))
            .ToListAsync(ct);

    public async Task<long> SumUserShareAsync(Guid groupId, Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => await _context.GroupBillSplits
            .Where(s => s.UserId == userId && _context.GroupBills.Any(b => b.Id == s.BillId && b.GroupId == groupId && b.OccurredAt >= start && b.OccurredAt < end))
            .SumAsync(s => s.ShareCents, ct);

    public async Task<DateTimeOffset?> LastReminderAtAsync(Guid groupId, Guid debtorUserId, CancellationToken ct = default)
        => await _context.GroupDebtReminders
            .Where(r => r.GroupId == groupId && r.DebtorUserId == debtorUserId)
            .OrderByDescending(r => r.SentAt)
            .Select(r => (DateTimeOffset?)r.SentAt)
            .FirstOrDefaultAsync(ct);
}
