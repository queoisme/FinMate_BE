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

public sealed record GroupTransactionListResult(IReadOnlyList<GroupTransactionDto> Items, string? NextCursor);

public interface IGroupTransactionService
{
    /// <summary>Chủ giao dịch chia sẻ, hoặc chuyển sang nhóm khác. Gọi lại với cùng nhóm để đổi danh mục nhóm.</summary>
    Task ShareAsync(Guid userId, Guid transactionId, Guid groupId, Guid? groupCategoryId, CancellationToken ct = default);

    Task UnshareAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Chế độ chỉ xem tổng: người không có quyền xem chi tiết chỉ thấy khoản của chính mình.</summary>
    Task<GroupTransactionListResult> ListAsync(Guid groupId, Guid userId, int year, int month, string? cursor, int limit, CancellationToken ct = default);

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
                var now = DateTimeOffset.UtcNow;

                // Khoản gốc có khoản hoàn/đảo: cả cụm đi vào nhóm cùng nhau và nhóm được tính lại (Phase 81, BR-RF-10).
                var raised = await MoveWithRefundsAsync(transaction, t => _ledger.MarkShared(t, target!, userId, now), token);
                if (raised is null)
                {
                    await _ledger.ApplyAsync(transaction, transaction.Category, -1, token);
                    _ledger.MarkShared(transaction, target!, userId, now);
                    raised = await _ledger.ApplyAsync(transaction, transaction.Category, +1, token);
                }

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

    public async Task<GroupTransactionListResult> ListAsync(Guid groupId, Guid userId, int year, int month, string? cursor, int limit, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (from, to) = VietnamTime.MonthRange(year, month);
        var onlyOwn = access.Has(GroupPermission.ViewMemberTransactions) ? (Guid?)null : userId;
        var page = await _finance.ListSharedTransactionsAsync(groupId, onlyOwn, from, to, cursor, GroupPaging.Clamp(limit, MaxLimit), ct);
        return new GroupTransactionListResult(page.Items.Select(ToDto).ToList(), page.NextCursor);
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

                var raised = await MoveWithRefundsAsync(transaction, t => t.GroupCategoryId = groupCategoryId, token);
                if (raised is null)
                {
                    await _ledger.ApplyAsync(transaction, transaction.Category, -1, token);
                    transaction.GroupCategoryId = groupCategoryId;
                    raised = await _ledger.ApplyAsync(transaction, transaction.Category, +1, token);
                }
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
        static void Unshare(Transaction t)
        {
            t.GroupId = null;
            t.GroupCategoryId = null;
            t.SharedAt = null;
        }

        if (await MoveWithRefundsAsync(transaction, Unshare, ct) is null)
        {
            await _ledger.ApplyAsync(transaction, transaction.Category, -1, ct);
            Unshare(transaction);
        }

        _groups.AddActivity(GroupService.NewActivity(groupId, actorUserId, GroupActivityType.TransactionUnshared, transaction.Id, DateTimeOffset.UtcNow));
        await _groups.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Phase 81, BR-RF-10: khoản hoàn/đảo không vào/ra nhóm hay đổi danh mục nhóm riêng, nó đi theo khoản gốc (422). Khoản gốc có khoản hoàn thì
    /// áp <paramref name="move"/> cho cả cụm và TÍNH LẠI các kỳ ngân sách nhóm cũ lẫn mới (không cộng trừ delta, D2). Không có khoản hoàn → null,
    /// caller đi đường cũ. Tra khoản hoàn theo chủ giao dịch (quyền với nhóm đã kiểm ở caller).
    /// </summary>
    private async Task<IReadOnlyList<GroupBudgetAlert>?> MoveWithRefundsAsync(Transaction transaction, Action<Transaction> move, CancellationToken ct)
    {
        if (transaction.Relation is not null)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.RefundFollowsOriginal,
                "Khoản hoàn đi theo khoản chi gốc: chia sẻ, bỏ chia sẻ hay đổi danh mục nhóm ở khoản chi gốc.");
        }

        if (transaction.TransactionType != TransactionType.Debit)
        {
            return null;
        }

        var related = await _transactions.ListRelatedAsync(transaction.UserId, transaction.Id, ct);
        if (related.Count == 0)
        {
            return null;
        }

        List<Transaction> cluster = [transaction, .. related];
        var before = cluster.Select(t => GroupScopeTouch.Of(t, t.Category)).OfType<GroupScopeTouch>().ToList();
        foreach (var member in cluster)
        {
            move(member);
        }

        var after = cluster.Select(t => GroupScopeTouch.Of(t, t.Category)).OfType<GroupScopeTouch>();
        return await _ledger.RecomputeAsync(cluster, before.Concat(after).Distinct().ToList(), ct);
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
