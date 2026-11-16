using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Refunds;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// S4a, D23, D24: đổi một khoản chi đã xác nhận của người ghi thành khoản chi từ quỹ (Transfer gắn quỹ chiều ra), và đổi ngược lại. Số dư ví không đổi
/// (Debit và Transfer chiều ra cùng trừ ví nguồn); ngân sách cá nhân được TÍNH LẠI qua <see cref="IRefundLedger"/> như Phase 83. Caller giữ khoá dòng
/// giao dịch và lưu; việc sau khi lưu (cache, cảnh báo, tóm tắt ngày, dự báo) chạy qua <see cref="AfterSaveAsync"/>.
/// </summary>
public interface IGroupFundSourceConverter
{
    /// <summary>D23: sai → 422 <c>GROUP_FUND_SOURCE_NOT_ELIGIBLE</c>. Gọi trước mọi kiểm tra khác của dòng, để lý do trả về là lý do thật.</summary>
    Task EnsureEligibleAsync(Guid userId, Transaction source, CancellationToken ct = default);

    /// <summary>Kiểm điều kiện (D23) rồi đổi hình dạng giao dịch. Không lưu.</summary>
    Task<RefundEffects> ConvertAsync(Guid userId, Transaction source, GroupFundEntry entry, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>D24: giao dịch về lại khoản chi với danh mục gốc lưu trên dòng quỹ (không dựa vào snapshot, vì snapshot bị dọn). Không lưu.</summary>
    Task<RefundEffects> RestoreAsync(Guid userId, Transaction personal, GroupFundEntry entry, CancellationToken ct = default);

    Task AfterSaveAsync(Guid userId, RefundEffects effects, CancellationToken ct = default);
}

public sealed class GroupFundSourceConverter : IGroupFundSourceConverter
{
    private readonly ITransactionRepository _transactions;
    private readonly IAnomalyRepository _anomalies;
    private readonly IMergeSnapshotRepository _snapshots;
    private readonly IRefundLedger _ledger;
    private readonly RefundAfterSave _afterSave;

    public GroupFundSourceConverter(
        ITransactionRepository transactions,
        IAnomalyRepository anomalies,
        IMergeSnapshotRepository snapshots,
        IRefundLedger ledger,
        IBudgetAlertNotifier budgetAlerts,
        ICacheService cache,
        IGroupLedger? groupLedger = null,
        IDailySummaryRefresher? summaries = null,
        IForecastAlertTrigger? forecastAlerts = null)
    {
        _transactions = transactions;
        _anomalies = anomalies;
        _snapshots = snapshots;
        _ledger = ledger;
        _afterSave = new RefundAfterSave(budgetAlerts, cache, groupLedger, summaries, forecastAlerts);
    }

    public async Task<RefundEffects> ConvertAsync(Guid userId, Transaction source, GroupFundEntry entry, DateTimeOffset now, CancellationToken ct = default)
    {
        await EnsureEligibleAsync(userId, source, ct);
        var before = RefundScopeSnapshot.Of(source);
        var row = SnapshotRow.Of(source);

        source.TransactionType = TransactionType.Transfer;
        source.CategoryId = null;
        source.Category = null;
        source.GroupFundEntryId = entry.Id;
        source.GroupFundDirection = GroupFundDirection.Out;
        source.UpdatedAt = now;

        var released = await ReleaseDraftRefundsAsync(userId, source, now, ct);
        await ReleaseSuspectedLinksAsync(userId, source, now, ct);
        var closed = await _anomalies.ResolveOpenForTransactionAsync(userId, source.Id, AnomalyResolution.Transfer, now, ct) > 0 ? [source.Id] : new List<Guid>();
        var effects = await _ledger.RecomputeAsync(userId, [source], [before], ct);

        _snapshots.Add(MergeSnapshotFactory.Create(
            userId, source.Id, null, MergeSnapshotKind.GroupFundExpense, null,
            new SnapshotBefore([row], new Dictionary<Guid, long>(), null, ReleasedDraftRefunds: released, ClosedAnomalies: closed),
            now));
        return effects;
    }

    public async Task<RefundEffects> RestoreAsync(Guid userId, Transaction personal, GroupFundEntry entry, CancellationToken ct = default)
    {
        var before = RefundScopeSnapshot.Of(personal);
        personal.TransactionType = TransactionType.Debit;
        personal.CategoryId = entry.OriginCategoryId;
        personal.GroupFundEntryId = null;
        personal.GroupFundDirection = null;
        personal.UpdatedAt = DateTimeOffset.UtcNow;
        return await _ledger.RecomputeAsync(userId, [personal], [before], ct);
    }

    public Task AfterSaveAsync(Guid userId, RefundEffects effects, CancellationToken ct = default) => _afterSave.RunAsync(userId, effects, ct);

    /// <summary>D23: khoản chi thật, đã xác nhận, của chính người ghi, chưa dính vào nhóm, quỹ, hoàn/đảo hay gộp trùng.</summary>
    public async Task EnsureEligibleAsync(Guid userId, Transaction source, CancellationToken ct = default)
    {
        var eligible = source.UserId == userId
            && source.DeletedAt is null
            && source.Status == TransactionStatus.Confirmed
            && source.TransactionType == TransactionType.Debit
            && source.GroupId is null
            && source.Relation is null
            && !source.IsReversed
            && source.MergedIntoTransactionId is null
            && source.DuplicateStatus != DuplicateStatus.Suspected
            && source.GroupFundEntryId is null
            && !LoanExclusion.IsLoan(source.Category);
        if (!eligible)
        {
            throw NotEligible("Chỉ chuyển được một khoản chi đã xác nhận của bạn, chưa chia sẻ nhóm, không phải hoàn tiền, cho vay hay khoản đang nghi trùng.");
        }

        if ((await _transactions.ListRelatedAsync(userId, source.Id, ct)).Any(r => r.Status == TransactionStatus.Confirmed))
        {
            throw NotEligible("Khoản chi này đã có khoản hoàn hoặc đảo nên không chuyển thành khoản chi từ quỹ được.");
        }
    }

    private async Task<List<Guid>> ReleaseDraftRefundsAsync(Guid userId, Transaction source, DateTimeOffset now, CancellationToken ct)
    {
        var released = new List<Guid>();
        foreach (var draft in await _transactions.ListRelatedAsync(userId, source.Id, ct))
        {
            draft.RelatedTransactionId = null;
            draft.Relation = null;
            draft.UpdatedAt = now;
            released.Add(draft.Id);
        }

        return released;
    }

    /// <summary>Nghi trùng trỏ tới khoản này được thả: nó không còn là khoản chi để trùng (cùng luật BR-DUP-08).</summary>
    private async Task ReleaseSuspectedLinksAsync(Guid userId, Transaction source, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var linked in await _transactions.ListDuplicateLinksToAsync(userId, source.Id, ct))
        {
            if (linked.DuplicateStatus == DuplicateStatus.Suspected)
            {
                linked.DuplicateOfTransactionId = null;
                linked.DuplicateStatus = null;
                linked.UpdatedAt = now;
            }
        }
    }

    private static BusinessRuleException NotEligible(string message) => new(GroupErrorCodes.FundSourceNotEligible, message);
}
