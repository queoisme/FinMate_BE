using FinMate.Application.Groups;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class DeleteTransactionCommandHandler : IDeleteTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly ICacheService _cache;
    private readonly IForecastAlertTrigger? _forecastAlerts;

    private readonly IGroupLedger? _groupLedger;
    private readonly Refunds.IRefundLedger? _refunds;
    private readonly IBudgetAlertNotifier? _budgetAlerts;
    private readonly IDailySummaryRefresher? _summaries;

    public DeleteTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        ICacheService cache,
        IForecastAlertTrigger? forecastAlerts = null,
        IGroupLedger? groupLedger = null,
        Refunds.IRefundLedger? refunds = null,
        IBudgetAlertNotifier? budgetAlerts = null,
        IDailySummaryRefresher? summaries = null)
    {
        _groupLedger = groupLedger;
        _refunds = refunds;
        _budgetAlerts = budgetAlerts;
        _summaries = summaries;
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _cache = cache;
        _forecastAlerts = forecastAlerts;
    }

    public async Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        var now = DateTimeOffset.UtcNow;
        var wasConfirmed = transaction.Status == TransactionStatus.Confirmed;

        // Phase 81 (BR-RF-11, D18): khoản gốc còn khoản hoàn/đảo ĐÃ XÁC NHẬN thì không xoá được; nháp đang trỏ tới nó chỉ mất liên kết.
        if (_refunds is not null && transaction.TransactionType == TransactionType.Debit)
        {
            await ReleaseDraftsOrRefuseAsync(command.UserId, transaction, now, ct);
        }

        // Xoá một khoản hoàn/đảo đã xác nhận: hoàn tác như giao dịch thường, nhưng ngân sách được TÍNH LẠI (D2) và dưới khoá khoản gốc.
        if (wasConfirmed && transaction.Relation is not null && _refunds is not null)
        {
            await DeleteRefundAsync(command.UserId, transaction, _refunds, now, ct);
            return;
        }

        if (wasConfirmed)
        {
            var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
            account.BalanceCents -= transaction.TransactionType == TransactionType.Credit
                ? transaction.AmountCents
                : -transaction.AmountCents;
            account.UpdatedAt = now;

            // Transfer đã cộng tiền vào ví đích lúc tạo — không trả lại thì tiền tự sinh ra.
            if (transaction.CounterAccountId is not null)
            {
                var counter = await _financialAccountRepository.GetByIdAsync(transaction.CounterAccountId.Value, command.UserId, ct)
                    ?? throw new NotFoundException("FinancialAccount", transaction.CounterAccountId.Value);
                counter.BalanceCents -= transaction.AmountCents;
                counter.UpdatedAt = now;
            }

            // Xoá chỉ sinh delta ÂM nên không bao giờ chạm ngưỡng cảnh báo — bỏ qua giá trị
            // trả về là có chủ ý, không phải quên.
            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                transaction.CategoryId,
                -TransactionBudgetDelta.Spend(transaction, transaction.Category),
                transaction.TransactedAt,
                ct);
        }

        // Hoàn phần của nhóm, kể cả khi chủ giao dịch đã rời nhóm: số của nhóm phải luôn khớp với các khoản còn lại.
        if (_groupLedger is not null)
        {
            await _groupLedger.ApplyAsync(transaction, transaction.Category, -1, ct);
        }

        transaction.DeletedAt = now;
        transaction.UpdatedAt = now;
        await ReleaseSuspectedLinksAsync(command.UserId, transaction.Id, now, ct);

        // account và budget period (nếu có) đã tracked cùng DbContext — UpdateAsync flush
        // atomically, xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);

        if (wasConfirmed)
        {
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);
        }
        await NotifyForecastAsync(command.UserId, ct);
    }

    /// <summary>
    /// Phase 82 (BR-DUP-08): khoản đang bị nghi là trùng với giao dịch này không còn gì để trùng nữa → bỏ nghi trùng (liên kết về null) trong cùng
    /// lượt lưu. Cặp đã bác giữ nguyên: liên kết tới một dòng đã xoá mềm không hại gì.
    /// </summary>
    private async Task ReleaseSuspectedLinksAsync(Guid userId, Guid deletedId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var linked in await _transactionRepository.ListDuplicateLinksToAsync(userId, deletedId, ct))
        {
            if (linked.DuplicateStatus == DuplicateStatus.Suspected)
            {
                linked.DuplicateOfTransactionId = null;
                linked.DuplicateStatus = null;
                linked.UpdatedAt = now;
            }
        }
    }

    private async Task ReleaseDraftsOrRefuseAsync(Guid userId, Domain.Entities.Transaction original, DateTimeOffset now, CancellationToken ct)
    {
        var related = await _transactionRepository.ListRelatedAsync(userId, original.Id, ct);
        if (related.Any(r => r.Status == TransactionStatus.Confirmed))
        {
            throw new ConflictException(
                TransactionErrorCodes.HasRefunds,
                "Khoản chi này đã có khoản hoàn hoặc đảo. Xoá các khoản đó trước rồi mới xoá khoản chi.");
        }

        foreach (var draft in related)
        {
            draft.RelatedTransactionId = null;
            draft.Relation = null;
            draft.UpdatedAt = now;
        }
    }

    private async Task DeleteRefundAsync(Guid userId, Domain.Entities.Transaction refund, Refunds.IRefundLedger refunds, DateTimeOffset now, CancellationToken ct)
    {
        async Task<Refunds.RefundEffects> DeleteAsync(Domain.Entities.Transaction? original)
        {
            var account = await _financialAccountRepository.GetByIdAsync(refund.FinancialAccountId, userId, ct)
                ?? throw new NotFoundException("FinancialAccount", refund.FinancialAccountId);
            account.BalanceCents -= refund.AmountCents;   // khoản hoàn là tiền vào: xoá thì lấy lại
            account.UpdatedAt = now;

            var before = new List<Refunds.RefundScopeSnapshot> { Refunds.RefundScopeSnapshot.Of(refund) };
            List<Domain.Entities.Transaction> pending = [refund];
            if (original is not null)
            {
                pending.Add(original);
                if (refund.Relation == TransactionRelation.Reversal)
                {
                    original.IsReversed = false;   // xoá lệnh đảo: khoản gốc trở lại mọi tổng hợp, ở kỳ của nó
                    original.UpdatedAt = now;
                    before.Add(Refunds.RefundScopeSnapshot.Of(original));
                }
            }

            refund.DeletedAt = now;
            refund.UpdatedAt = now;
            var effects = await refunds.RecomputeAsync(userId, pending, before, ct);
            await _transactionRepository.UpdateAsync(refund, ct);
            return effects;
        }

        var effects = refund.RelatedTransactionId is { } originalId
            ? await _transactionRepository.RunWithOriginalLockedAsync(userId, originalId, original => DeleteAsync(original), ct)
            : await DeleteAsync(null);

        if (_budgetAlerts is not null)
        {
            await new RefundAfterSave(_budgetAlerts, _cache, _groupLedger, _summaries, _forecastAlerts).RunAsync(userId, effects, ct);
        }
        else
        {
            await NotifyForecastAsync(userId, ct);
        }
    }

    /// <summary>Kiểm tra số dư thấp / vượt ngân sách sớm ngoài request (Flow 6). Gọi SAU khi đã lưu; trigger tự nuốt lỗi.</summary>
    private Task NotifyForecastAsync(Guid userId, CancellationToken ct)
        => _forecastAlerts is null ? Task.CompletedTask : _forecastAlerts.UserChangedAsync(userId, ct);
}
