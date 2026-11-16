using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;

namespace FinMate.Tests.Integration;

/// <summary>
/// Gieo thẳng vào DB một nhóm, một quỹ, một dòng quỹ và giao dịch cá nhân gắn với nó (Phase 85), cho các test cần transfer gắn quỹ trước khi có API.
/// Đổi số dư ví theo đúng vế của giao dịch, như đường ghi thật sẽ làm.
/// </summary>
internal static class GroupFundSeed
{
    public sealed record Seeded(Guid GroupId, Guid FundId, Guid EntryId, Guid TransactionId);

    public static async Task<Seeded> FundTransferAsync(
        FinMateDbContext db, Guid userId, Guid accountId, GroupFundDirection direction, long amountCents = 500_000, DateTimeOffset? at = null,
        string fundName = "Tiền điện nước")
    {
        var now = DateTimeOffset.UtcNow;
        var when = at ?? now;
        var group = new Group { Id = Guid.NewGuid(), Name = "Phòng trọ", CreatedByUserId = userId, CreatedAt = now, UpdatedAt = now };
        var fund = new GroupFund { Id = Guid.NewGuid(), GroupId = group.Id, Name = fundName, CreatedByUserId = userId, CreatedAt = now, UpdatedAt = now };
        var kind = direction == GroupFundDirection.Out ? GroupFundEntryKind.Contribution : GroupFundEntryKind.Withdrawal;
        var entry = new GroupFundEntry
        {
            Id = Guid.NewGuid(), FundId = fund.Id, GroupId = group.Id, MemberUserId = userId, Kind = kind, AmountCents = amountCents,
            TransactedAt = when, CreatedAt = now, UpdatedAt = now,
        };
        fund.BalanceCents = kind == GroupFundEntryKind.Contribution ? amountCents : -amountCents;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = accountId, AmountCents = amountCents, TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual, Status = TransactionStatus.Confirmed, TransactedAt = when, CreatedAt = now, UpdatedAt = now,
            GroupFundEntryId = entry.Id, GroupFundDirection = direction,
        };

        db.Groups.Add(group);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = userId, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
        db.GroupFunds.Add(fund);
        db.GroupFundEntries.Add(entry);
        db.Transactions.Add(transaction);
        var account = await db.FinancialAccounts.FindAsync(accountId) ?? throw new InvalidOperationException("Ví không tồn tại.");
        account.BalanceCents += direction == GroupFundDirection.Out ? -amountCents : amountCents;
        await db.SaveChangesAsync();
        return new Seeded(group.Id, fund.Id, entry.Id, transaction.Id);
    }

    /// <summary>Gieo một khoản chi từ quỹ (không kèm sổ cá nhân) và trừ số dư quỹ.</summary>
    public static async Task<Guid> ExpenseAsync(
        FinMateDbContext db, Guid groupId, Guid fundId, Guid? memberUserId, long amountCents, Guid? categoryId, Guid? groupCategoryId = null,
        DateTimeOffset? at = null, DateTimeOffset? deletedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new GroupFundEntry
        {
            Id = Guid.NewGuid(), FundId = fundId, GroupId = groupId, MemberUserId = memberUserId, Kind = GroupFundEntryKind.Expense, AmountCents = amountCents,
            CategoryId = categoryId, GroupCategoryId = groupCategoryId, TransactedAt = at ?? now, CreatedAt = now, UpdatedAt = now, DeletedAt = deletedAt,
        };
        db.GroupFundEntries.Add(entry);
        if (deletedAt is null)
        {
            var fund = await db.GroupFunds.FindAsync(fundId) ?? throw new InvalidOperationException("Quỹ không tồn tại.");
            fund.BalanceCents -= amountCents;
        }

        await db.SaveChangesAsync();
        return entry.Id;
    }
}
