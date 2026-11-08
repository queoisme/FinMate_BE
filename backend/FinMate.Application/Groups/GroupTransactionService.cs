using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <summary>Cố ý không có ví, số dư sau giao dịch, nhật ký thông báo hay mã phía client: đó là dữ liệu riêng của người chia sẻ.</summary>
public sealed record GroupTransactionDto(
    Guid Id,
    Guid MemberUserId,
    string MemberName,
    long AmountCents,
    TransactionType TransactionType,
    string? MerchantName,
    string? Description,
    DateTimeOffset TransactedAt,
    DateTimeOffset? SharedAt,
    Guid? GroupCategoryId,
    string CategoryName);

public interface IGroupTransactionService
{
    /// <summary>Chủ giao dịch chia sẻ, hoặc chuyển sang nhóm khác. Gọi lại với cùng nhóm để đổi danh mục nhóm.</summary>
    Task ShareAsync(Guid userId, Guid transactionId, Guid groupId, Guid? groupCategoryId, CancellationToken ct = default);

    Task UnshareAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Chế độ chỉ xem tổng: người không có quyền xem chi tiết chỉ thấy khoản của chính mình.</summary>
    Task<IReadOnlyList<GroupTransactionDto>> ListAsync(Guid groupId, Guid userId, int year, int month, int limit, CancellationToken ct = default);

    Task ReclassifyAsync(Guid groupId, Guid userId, Guid transactionId, Guid? groupCategoryId, CancellationToken ct = default);
    Task RemoveAsync(Guid groupId, Guid userId, Guid transactionId, CancellationToken ct = default);
}

public class GroupTransactionService : IGroupTransactionService
{
    internal const string UncategorizedName = "Chưa phân loại";
    private const int MaxLimit = 200;

    private readonly ITransactionRepository _transactions;
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IGroupLedger _ledger;
    private readonly IConcurrencyRetry _retry;

    public GroupTransactionService(
        ITransactionRepository transactions,
        IGroupFinanceRepository finance,
        IGroupRepository groups,
        IGroupAccessService access,
        IGroupLedger ledger,
        IConcurrencyRetry retry)
    {
        _transactions = transactions;
        _finance = finance;
        _groups = groups;
        _access = access;
        _ledger = ledger;
        _retry = retry;
    }

    public async Task ShareAsync(Guid userId, Guid transactionId, Guid groupId, Guid? groupCategoryId, CancellationToken ct = default)
    {
        var alerts = await _retry.ExecuteAsync(
            async token =>
            {
                var transaction = await RequireOwnAsync(userId, transactionId, token);
                var target = await _ledger.ResolveAsync(userId, groupId, groupCategoryId, transaction, transaction.Category, token);

                await _ledger.ApplyAsync(transaction, transaction.Category, -1, token);
                _ledger.MarkShared(transaction, target!, userId, DateTimeOffset.UtcNow);
                var raised = await _ledger.ApplyAsync(transaction, transaction.Category, +1, token);

                await _groups.SaveChangesAsync(token);
                return raised;
            },
            ct);
        await _ledger.SendAlertsAsync(alerts, ct);
    }

    public Task UnshareAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => _retry.ExecuteAsync(
            async token =>
            {
                var transaction = await RequireOwnAsync(userId, transactionId, token);
                if (transaction.GroupId is { } groupId)
                {
                    // Người đã rời nhóm không tự gỡ được: lịch sử của nhóm được giữ nguyên khi rời. Họ vẫn có thể xóa hẳn giao dịch của mình.
                    await _access.RequireMemberAsync(groupId, userId, token);
                    await DetachAsync(transaction, groupId, userId, token);
                }

                return true;
            },
            ct);

    public async Task<IReadOnlyList<GroupTransactionDto>> ListAsync(Guid groupId, Guid userId, int year, int month, int limit, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (from, to) = VietnamTime.MonthRange(new DateTimeOffset(year, month, 1, 12, 0, 0, VietnamTime.Offset));
        var onlyOwn = access.Has(GroupPermission.ViewMemberTransactions) ? (Guid?)null : userId;
        var rows = await _finance.ListSharedTransactionsAsync(groupId, onlyOwn, from, to, Math.Clamp(limit, 1, MaxLimit), ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task ReclassifyAsync(Guid groupId, Guid userId, Guid transactionId, Guid? groupCategoryId, CancellationToken ct = default)
    {
        var alerts = await _retry.ExecuteAsync(
            async token =>
            {
                await _access.RequireAsync(groupId, userId, GroupPermission.ManageSharedTransactions, token);
                var transaction = await _finance.GetSharedTransactionAsync(groupId, transactionId, token) ?? throw new NotFoundException("Transaction", transactionId);
                if (groupCategoryId is { } id && await _finance.GetCategoryAsync(groupId, id, token) is null)
                {
                    throw new NotFoundException("GroupCategory", id);
                }

                await _ledger.ApplyAsync(transaction, transaction.Category, -1, token);
                transaction.GroupCategoryId = groupCategoryId;
                var raised = await _ledger.ApplyAsync(transaction, transaction.Category, +1, token);
                _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.TransactionReclassified, transactionId, DateTimeOffset.UtcNow));
                await _groups.SaveChangesAsync(token);
                return raised;
            },
            ct);
        await _ledger.SendAlertsAsync(alerts, ct);
    }

    public Task RemoveAsync(Guid groupId, Guid userId, Guid transactionId, CancellationToken ct = default)
        => _retry.ExecuteAsync(
            async token =>
            {
                await _access.RequireAsync(groupId, userId, GroupPermission.ManageSharedTransactions, token);
                var transaction = await _finance.GetSharedTransactionAsync(groupId, transactionId, token) ?? throw new NotFoundException("Transaction", transactionId);
                await DetachAsync(transaction, groupId, userId, token);
                return true;
            },
            ct);

    private async Task DetachAsync(Transaction transaction, Guid groupId, Guid actorUserId, CancellationToken ct)
    {
        await _ledger.ApplyAsync(transaction, transaction.Category, -1, ct);
        transaction.GroupId = null;
        transaction.GroupCategoryId = null;
        transaction.SharedAt = null;
        _groups.AddActivity(GroupService.NewActivity(groupId, actorUserId, GroupActivityType.TransactionUnshared, transaction.Id, DateTimeOffset.UtcNow));
        await _groups.SaveChangesAsync(ct);
    }

    private async Task<Transaction> RequireOwnAsync(Guid userId, Guid transactionId, CancellationToken ct)
        => await _transactions.GetByIdAsync(transactionId, userId, ct) ?? throw new NotFoundException("Transaction", transactionId);

    private static GroupTransactionDto ToDto(SharedTransactionView view)
    {
        var t = view.Transaction;
        return new GroupTransactionDto(
            t.Id,
            t.UserId,
            view.IsUserActive ? view.MemberName : GroupMemberService.InactiveMemberName,
            t.AmountCents,
            t.TransactionType,
            t.MerchantName,
            t.Description,
            t.TransactedAt,
            t.SharedAt,
            t.GroupCategoryId,
            t.GroupCategory?.Name ?? (t.Category is { IsSystem: true } ? t.Category.Name : UncategorizedName));
    }
}
