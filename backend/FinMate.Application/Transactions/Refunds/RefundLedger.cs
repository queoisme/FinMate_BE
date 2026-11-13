using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Groups;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Refunds;

/// <summary>Việc phải làm SAU khi lưu: push cảnh báo (nếu có) và chốt lại các ngày đã tóm tắt bị ảnh hưởng (D7).</summary>
public sealed record RefundEffects(
    IReadOnlyList<BudgetAlert> BudgetAlerts,
    IReadOnlyList<GroupBudgetAlert> GroupAlerts,
    IReadOnlyCollection<DateOnly> TouchedDates)
{
    public static readonly RefundEffects None = new([], [], []);
}

/// <summary>
/// Gắn và gỡ quan hệ hoàn/đảo (Phase 81). Mọi đường đều qua đây: tạo khoản hoàn từ khoản gốc, liên kết một khoản thu có sẵn, xác nhận nháp
/// có liên kết, gỡ liên kết. Chỉ đổi dữ liệu đang được theo dõi và tính lại các kỳ ngân sách bị chạm; KHÔNG lưu, KHÔNG đụng số dư (tạo mới thì
/// handler cộng số dư, liên kết một khoản đã xác nhận thì số dư không đổi). Caller phải đang giữ khoá dòng khoản gốc
/// (<see cref="ITransactionRepository.RunWithOriginalLockedAsync{T}"/>) để tổng đã hoàn đọc ở đây là số mới nhất.
/// </summary>
public interface IRefundLedger
{
    /// <param name="refund">Khoản tiền vào sẽ mang quan hệ: mới (chưa lưu) hoặc có sẵn (nháp hay đã xác nhận).</param>
    /// <param name="before">Phạm vi ngân sách khoản này đang chạm TRƯỚC khi gắn (khoản có sẵn đã xác nhận): để kỳ cũ cũng được tính lại.</param>
    Task<RefundEffects> AttachAsync(
        Guid userId, Transaction original, Transaction refund, TransactionRelation relation, RefundScopeSnapshot? before, CancellationToken ct = default);

    /// <summary>
    /// Gỡ quan hệ: khoản hoàn thành khoản thu thường (danh mục <paramref name="incomeCategory"/>); gỡ một lệnh đảo thì khoản gốc hết bị đảo.
    /// <paramref name="original"/> null với hoàn mồ côi.
    /// </summary>
    Task<RefundEffects> DetachAsync(Guid userId, Transaction? original, Transaction refund, Category? incomeCategory, CancellationToken ct = default);

    /// <summary>Biến một khoản tiền vào thành hoàn "mồ côi" (BR-RF-09): không có khoản gốc, người dùng tự chọn danh mục chi.</summary>
    Task<RefundEffects> MarkOrphanAsync(Guid userId, Transaction refund, Category category, RefundScopeSnapshot? before, CancellationToken ct = default);

    /// <summary>Tính lại sau một thay đổi khác trên khoản hoàn/khoản gốc (sửa số tiền, ngày, danh mục, xoá) mà handler đã áp vào entity.</summary>
    Task<RefundEffects> RecomputeAsync(
        Guid userId, IReadOnlyCollection<Transaction> pending, IReadOnlyCollection<RefundScopeSnapshot> before, CancellationToken ct = default);
}

/// <summary>Ảnh chụp phạm vi ngân sách (cá nhân và nhóm) mà một giao dịch chạm vào ở một thời điểm, chụp TRƯỚC khi sửa nó.</summary>
public sealed record RefundScopeSnapshot(Guid? CategoryId, DateTimeOffset At, GroupScopeTouch? Group)
{
    public static RefundScopeSnapshot Of(Transaction transaction)
        => new(transaction.CategoryId, transaction.TransactedAt, GroupScopeTouch.Of(transaction, transaction.Category));
}

public sealed class RefundLedger : IRefundLedger
{
    private readonly ITransactionRepository _transactions;
    private readonly IBudgetPeriodService _budgets;
    private readonly IGroupLedger? _groupLedger;
    private readonly IRefundFollowUps? _followUps;

    public RefundLedger(ITransactionRepository transactions, IBudgetPeriodService budgets, IGroupLedger? groupLedger = null, IRefundFollowUps? followUps = null)
    {
        _transactions = transactions;
        _budgets = budgets;
        _groupLedger = groupLedger;
        _followUps = followUps;
    }

    public async Task<RefundEffects> AttachAsync(
        Guid userId, Transaction original, Transaction refund, TransactionRelation relation, RefundScopeSnapshot? before, CancellationToken ct = default)
    {
        RefundPolicy.EnsureCanCarryRelation(refund, original.Id);

        // Khoản đang là nháp chưa tính vào đâu: chỉ cần còn chỗ cho nó khi được xác nhận. Khoản đã/sắp xác nhận thì kiểm đủ BR-RF-02.
        var alreadyRefunded = await _transactions.SumConfirmedRefundsAsync(userId, original.Id, refund.Id == Guid.Empty ? null : refund.Id, ct);
        RefundPolicy.EnsureLinkable(original, relation, refund.AmountCents, alreadyRefunded);

        var snapshotOriginal = RefundScopeSnapshot.Of(original);
        refund.Relation = relation;
        refund.RelatedTransactionId = original.Id;

        // BR-RF-03: danh mục của khoản gốc, không phải "thu nhập". BR-RF-10: nhóm và danh mục nhóm của khoản gốc.
        refund.CategoryId = original.CategoryId;
        refund.Category = original.Category;
        refund.GroupId = original.GroupId;
        refund.GroupCategoryId = original.GroupCategoryId;
        refund.SharedAt = original.GroupId is null ? null : refund.SharedAt ?? DateTimeOffset.UtcNow;

        if (relation == TransactionRelation.Reversal && refund.Status == TransactionStatus.Confirmed)
        {
            original.IsReversed = true;
            original.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (refund.Status != TransactionStatus.Confirmed)
        {
            return RefundEffects.None;   // nháp: chưa tác động gì, sẽ tính khi xác nhận
        }

        var snapshots = new List<RefundScopeSnapshot> { RefundScopeSnapshot.Of(refund) };
        if (before is not null)
        {
            snapshots.Add(before);
        }

        if (relation == TransactionRelation.Reversal)
        {
            snapshots.Add(snapshotOriginal);   // D1/R4: hoàn tác khoản gốc ở kỳ CỦA NÓ
        }

        if (_followUps is not null)
        {
            await _followUps.OnRefundedAsync(userId, original, alreadyRefunded + refund.AmountCents, ct);   // R1, R2
        }

        return await RecomputeAsync(userId, [refund, original], snapshots, ct);
    }

    public async Task<RefundEffects> DetachAsync(Guid userId, Transaction? original, Transaction refund, Category? incomeCategory, CancellationToken ct = default)
    {
        var before = RefundScopeSnapshot.Of(refund);
        var wasReversal = refund.Relation == TransactionRelation.Reversal;

        refund.Relation = null;
        refund.RelatedTransactionId = null;
        refund.CategoryId = incomeCategory?.Id;
        refund.Category = incomeCategory;
        refund.UpdatedAt = DateTimeOffset.UtcNow;

        if (wasReversal && original is not null && refund.Status == TransactionStatus.Confirmed)
        {
            original.IsReversed = false;
            original.UpdatedAt = refund.UpdatedAt;
        }

        if (refund.Status != TransactionStatus.Confirmed)
        {
            return RefundEffects.None;
        }

        var snapshots = new List<RefundScopeSnapshot> { before, RefundScopeSnapshot.Of(refund) };
        List<Transaction> pending = [refund];
        if (original is not null)
        {
            pending.Add(original);
            if (wasReversal)
            {
                snapshots.Add(RefundScopeSnapshot.Of(original));
            }
        }

        return await RecomputeAsync(userId, pending, snapshots, ct);
    }

    public async Task<RefundEffects> MarkOrphanAsync(Guid userId, Transaction refund, Category category, RefundScopeSnapshot? before, CancellationToken ct = default)
    {
        RefundPolicy.EnsureCanCarryRelation(refund, null);
        RefundPolicy.EnsureOrphanCategory(category);

        refund.Relation = TransactionRelation.Refund;
        refund.RelatedTransactionId = null;
        refund.CategoryId = category.Id;
        refund.Category = category;
        refund.UpdatedAt = DateTimeOffset.UtcNow;

        if (refund.Status != TransactionStatus.Confirmed)
        {
            return RefundEffects.None;
        }

        var snapshots = new List<RefundScopeSnapshot> { RefundScopeSnapshot.Of(refund) };
        if (before is not null)
        {
            snapshots.Add(before);
        }

        return await RecomputeAsync(userId, [refund], snapshots, ct);
    }

    public async Task<RefundEffects> RecomputeAsync(
        Guid userId, IReadOnlyCollection<Transaction> pending, IReadOnlyCollection<RefundScopeSnapshot> before, CancellationToken ct = default)
    {
        var all = before.Concat(pending.Select(RefundScopeSnapshot.Of)).ToList();

        var budgetAlerts = await _budgets.RecomputeAsync(userId, pending, all.Select(s => (s.CategoryId, s.At)).Distinct().ToList(), ct);

        IReadOnlyList<GroupBudgetAlert> groupAlerts = [];
        var groupTouches = all.Where(s => s.Group is not null).Select(s => s.Group!).Distinct().ToList();
        if (_groupLedger is not null && groupTouches.Count > 0)
        {
            groupAlerts = await _groupLedger.RecomputeAsync(pending, groupTouches, ct);
        }

        var dates = all.Select(s => VietnamTime.DateOf(s.At)).Distinct().ToList();
        return new RefundEffects(budgetAlerts, groupAlerts, dates);
    }
}
