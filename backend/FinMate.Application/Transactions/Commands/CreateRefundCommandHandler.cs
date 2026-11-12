using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Groups;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateRefundCommandHandler : ICreateRefundCommandHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;
    private readonly IRefundLedger _ledger;
    private readonly IBudgetAlertNotifier _budgetAlerts;
    private readonly ICacheService _cache;
    private readonly IValidator<CreateRefundCommand> _validator;
    private readonly IGroupLedger? _groupLedger;
    private readonly IDailySummaryRefresher? _summaries;
    private readonly IForecastAlertTrigger? _forecastAlerts;

    public CreateRefundCommandHandler(
        ITransactionRepository transactions,
        IFinancialAccountRepository accounts,
        IRefundLedger ledger,
        IBudgetAlertNotifier budgetAlerts,
        ICacheService cache,
        IValidator<CreateRefundCommand> validator,
        IGroupLedger? groupLedger = null,
        IDailySummaryRefresher? summaries = null,
        IForecastAlertTrigger? forecastAlerts = null)
    {
        _transactions = transactions;
        _accounts = accounts;
        _ledger = ledger;
        _budgetAlerts = budgetAlerts;
        _cache = cache;
        _validator = validator;
        _groupLedger = groupLedger;
        _summaries = summaries;
        _forecastAlerts = forecastAlerts;
    }

    public async Task<TransactionCreationResult> HandleAsync(CreateRefundCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Đọc, tính, lưu một lần trong lúc giữ khoá dòng khoản gốc; mọi việc bên ngoài (push, chốt tóm tắt, dự báo) chạy sau khi đã commit.
        var (result, effects) = await _transactions.RunWithOriginalLockedAsync(
            command.UserId, command.OriginalTransactionId, original => CreateAsync(command, original, ct), ct);

        if (!result.AlreadyExisted)
        {
            await new RefundAfterSave(_budgetAlerts, _cache, _groupLedger, _summaries, _forecastAlerts).RunAsync(command.UserId, effects, ct);
        }

        return result;
    }

    private async Task<(TransactionCreationResult, RefundEffects)> CreateAsync(CreateRefundCommand command, Transaction original, CancellationToken ct)
    {
        // Kiểm gửi lại SAU khi đã khoá: hai lần gửi cùng clientRequestId cùng lúc xếp hàng ở đây, lần sau thấy bản của lần trước.
        if (command.ClientRequestId is { } clientRequestId
            && await _transactions.GetByClientRequestIdAsync(command.UserId, clientRequestId, ct) is { } existing)
        {
            var existingRefunded = await RefundedOfAsync(command.UserId, existing, ct);
            return (new TransactionCreationResult(TransactionMapper.ToDto(existing, existingRefunded), AlreadyExisted: true), RefundEffects.None);
        }

        var relation = command.Relation ?? TransactionRelation.Refund;
        var refunded = await _transactions.SumConfirmedRefundsAsync(command.UserId, original.Id, null, ct);
        var amount = command.AmountCents
            ?? (relation == TransactionRelation.Reversal ? original.AmountCents : RefundPolicy.RemainingRefundable(original, refunded));
        if (amount <= 0)
        {
            // Mặc định "phần còn lại" mà không còn gì: nói đúng lý do thay vì tạo một khoản 0 đồng.
            RefundPolicy.EnsureLinkable(original, relation, 1, original.AmountCents);
        }

        var accountId = command.FinancialAccountId ?? original.FinancialAccountId;
        var account = await _accounts.GetByIdAsync(accountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", accountId);

        var now = DateTimeOffset.UtcNow;
        var refund = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            ClientRequestId = command.ClientRequestId,
            FinancialAccountId = account.Id,
            AmountCents = amount,
            TransactionType = TransactionType.Credit,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            MerchantName = original.MerchantName,
            TransactedAt = command.TransactedAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var effects = await _ledger.AttachAsync(command.UserId, original, refund, relation, before: null, ct);

        account.BalanceCents += amount;   // tiền vào ví (BR-RF-01); số dư không phân biệt hoàn hay thu
        account.UpdatedAt = now;
        refund.BalanceAfterCents = account.BalanceCents;

        await _transactions.AddAsync(refund, ct);
        return (new TransactionCreationResult(TransactionMapper.ToDto(refund), AlreadyExisted: false), effects);
    }

    private async Task<long?> RefundedOfAsync(Guid userId, Transaction transaction, CancellationToken ct)
        => transaction.TransactionType == TransactionType.Debit ? await _transactions.SumConfirmedRefundsAsync(userId, transaction.Id, null, ct) : null;
}

public class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.AmountCents).GreaterThan(0).When(x => x.AmountCents is not null);
        RuleFor(x => x.Relation).IsInEnum().When(x => x.Relation is not null);
    }
}
