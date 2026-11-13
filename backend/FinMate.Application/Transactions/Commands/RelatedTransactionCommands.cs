using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// <c>PUT /transactions/{id}/related</c> (Phase 81): biến khoản tiền vào <paramref name="TransactionId"/> (nháp hoặc đã xác nhận) thành khoản
/// hoàn/đảo của <paramref name="RelatedTransactionId"/>. <paramref name="RelatedTransactionId"/> null + <c>Refund</c> = hoàn "mồ côi", khi đó
/// bắt buộc <paramref name="CategoryId"/> là một danh mục chi (BR-RF-09). Không giới hạn thời gian (D12).
/// </summary>
public record LinkRelatedTransactionCommand(
    Guid UserId, Guid TransactionId, Guid? RelatedTransactionId, TransactionRelation Relation, Guid? CategoryId = null);

/// <summary><c>DELETE /transactions/{id}/related</c>: gỡ quan hệ, khoản đó trở lại thành thu nhập thường (danh mục "Thu nhập").</summary>
public record UnlinkRelatedTransactionCommand(Guid UserId, Guid TransactionId);

public interface ILinkRelatedTransactionCommandHandler
{
    Task<TransactionDto> HandleAsync(LinkRelatedTransactionCommand command, CancellationToken ct = default);
}

public interface IUnlinkRelatedTransactionCommandHandler
{
    Task<TransactionDto> HandleAsync(UnlinkRelatedTransactionCommand command, CancellationToken ct = default);
}

/// <summary>Cả hai thao tác: đọc, tính, lưu MỘT lần (hoàn tác "thu nhập" cũ và áp "hoàn tiền" trong cùng một SaveChanges), việc bên ngoài chạy sau.</summary>
public class RelatedTransactionCommandHandler : ILinkRelatedTransactionCommandHandler, IUnlinkRelatedTransactionCommandHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IRefundLedger _ledger;
    private readonly RefundAfterSave _afterSave;

    public RelatedTransactionCommandHandler(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IRefundLedger ledger,
        IBudgetAlertNotifier budgetAlerts,
        ICacheService cache,
        IGroupLedger? groupLedger = null,
        IDailySummaryRefresher? summaries = null,
        IForecastAlertTrigger? forecastAlerts = null)
    {
        _transactions = transactions;
        _categories = categories;
        _ledger = ledger;
        _afterSave = new RefundAfterSave(budgetAlerts, cache, groupLedger, summaries, forecastAlerts);
    }

    public async Task<TransactionDto> HandleAsync(LinkRelatedTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactions.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);
        var before = transaction.Status == TransactionStatus.Confirmed ? RefundScopeSnapshot.Of(transaction) : null;

        RefundEffects effects;
        if (command.RelatedTransactionId is not { } originalId)
        {
            effects = await MarkOrphanAsync(command, transaction, before, ct);
        }
        else
        {
            if (transaction.RelatedTransactionId is { } current && current != originalId)
            {
                throw new BusinessRuleException(
                    TransactionErrorCodes.RefundNotLinkable,
                    "Khoản này đang liên kết với một khoản chi khác. Gỡ liên kết trước rồi liên kết lại.");
            }

            effects = await _transactions.RunWithOriginalLockedAsync(command.UserId, originalId, async original =>
            {
                var result = await _ledger.AttachAsync(command.UserId, original, transaction, command.Relation, before, ct);
                transaction.UpdatedAt = DateTimeOffset.UtcNow;
                await _transactions.UpdateAsync(transaction, ct);
                return result;
            }, ct);
        }

        await _afterSave.RunAsync(command.UserId, effects, ct);
        return TransactionMapper.ToDto(transaction);
    }

    public async Task<TransactionDto> HandleAsync(UnlinkRelatedTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactions.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);
        if (transaction.Relation is null)
        {
            return TransactionMapper.ToDto(transaction);   // không có gì để gỡ: idempotent
        }

        var income = await _categories.GetSystemBySlugAsync(RefundPolicy.IncomeSlug, ct);
        RefundEffects effects;
        if (transaction.RelatedTransactionId is { } originalId)
        {
            effects = await _transactions.RunWithOriginalLockedAsync(command.UserId, originalId, async original =>
            {
                var result = await _ledger.DetachAsync(command.UserId, original, transaction, income, ct);
                await _transactions.UpdateAsync(transaction, ct);
                return result;
            }, ct);
        }
        else
        {
            effects = await _ledger.DetachAsync(command.UserId, null, transaction, income, ct);
            await _transactions.UpdateAsync(transaction, ct);
        }

        await _afterSave.RunAsync(command.UserId, effects, ct);
        return TransactionMapper.ToDto(transaction);
    }

    private async Task<RefundEffects> MarkOrphanAsync(LinkRelatedTransactionCommand command, Transaction transaction, RefundScopeSnapshot? before, CancellationToken ct)
    {
        if (command.Relation != TransactionRelation.Refund)
        {
            throw new BusinessRuleException(TransactionErrorCodes.RefundNotLinkable, "Đảo giao dịch luôn phải chọn khoản chi bị đảo.");
        }

        var category = command.CategoryId is { } categoryId ? await _categories.GetByIdAsync(categoryId, ct) : null;
        if (category is not null && category.UserId is not null && category.UserId != command.UserId)
        {
            throw new NotFoundException("Category", command.CategoryId!.Value);
        }

        if (transaction.RelatedTransactionId is not null)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.RefundNotLinkable,
                "Khoản này đang liên kết với một khoản chi. Gỡ liên kết trước rồi đánh dấu là hoàn không có khoản gốc.");
        }

        var effects = await _ledger.MarkOrphanAsync(command.UserId, transaction, category!, before, ct);
        await _transactions.UpdateAsync(transaction, ct);
        return effects;
    }
}

/// <summary>Các việc sau khi lưu một thao tác hoàn/đảo, dùng chung giữa các handler: bỏ cache tóm tắt, push, chốt lại tóm tắt ngày, báo dự báo.</summary>
public sealed class RefundAfterSave(
    IBudgetAlertNotifier budgetAlerts,
    ICacheService cache,
    IGroupLedger? groupLedger,
    IDailySummaryRefresher? summaries,
    IForecastAlertTrigger? forecastAlerts)
{
    public async Task RunAsync(Guid userId, RefundEffects effects, CancellationToken ct)
    {
        foreach (var date in effects.TouchedDates)
        {
            await TransactionBudgetDelta.InvalidateSummaryAsync(cache, userId, new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)), ct);
        }

        await budgetAlerts.SendAsync(effects.BudgetAlerts, ct);
        if (groupLedger is not null)
        {
            await groupLedger.SendAlertsAsync(effects.GroupAlerts, ct);
        }

        if (summaries is not null && effects.TouchedDates.Count > 0)
        {
            await summaries.RefreshAsync(userId, effects.TouchedDates, ct);
        }

        if (forecastAlerts is not null)
        {
            await forecastAlerts.UserChangedAsync(userId, ct);
        }
    }
}
