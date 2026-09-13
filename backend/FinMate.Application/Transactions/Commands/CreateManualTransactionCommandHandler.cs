using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Application.Gamification;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateManualTransactionCommandHandler : ICreateManualTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly IGamificationService _gamificationService;
    private readonly ICacheService _cache;
    private readonly IValidator<CreateManualTransactionCommand> _validator;

    public CreateManualTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IBudgetPeriodService budgetPeriodService,
        IBudgetAlertNotifier budgetAlertNotifier,
        IGamificationService gamificationService,
        ICacheService cache,
        IValidator<CreateManualTransactionCommand> validator)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _budgetPeriodService = budgetPeriodService;
        _budgetAlertNotifier = budgetAlertNotifier;
        _gamificationService = gamificationService;
        _cache = cache;
        _validator = validator;
    }

    public async Task<TransactionDto> HandleAsync(CreateManualTransactionCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var account = await _financialAccountRepository.GetByIdAsync(command.FinancialAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.FinancialAccountId);

        Category? category = null;
        if (command.CategoryId is not null)
        {
            category = await _categoryRepository.GetByIdAsync(command.CategoryId.Value, ct);
            if (category is null || (category.UserId is not null && category.UserId != command.UserId))
            {
                throw new NotFoundException("Category", command.CategoryId.Value);
            }
        }

        var now = DateTimeOffset.UtcNow;
        account.BalanceCents += command.TransactionType == TransactionType.Credit
            ? command.AmountCents
            : -command.AmountCents;
        account.UpdatedAt = now;

        var transaction = new Transaction
        {
            UserId = command.UserId,
            FinancialAccountId = account.Id,
            CategoryId = category?.Id,
            AmountCents = command.AmountCents,
            TransactionType = command.TransactionType,
            Source = command.Source,
            Status = TransactionStatus.Confirmed,
            MerchantName = command.MerchantName,
            Description = command.Description,
            TransactedAt = command.TransactedAt,
            BalanceAfterCents = account.BalanceCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Giao dịch thủ công vào thẳng Status=Confirmed (không qua bước confirm riêng) nên
        // phải tiêu hạn mức ngay tại đây, không phải ở ConfirmTransactionCommandHandler.
        var budgetAlerts = await _budgetPeriodService.ApplyDeltaAsync(
            command.UserId,
            category?.Id,
            TransactionBudgetDelta.Spend(command.TransactionType, command.AmountCents),
            command.TransactedAt,
            ct);

        await _gamificationService.RecordActivityAsync(
            new GamificationActivity(
                command.UserId,
                MissionConditionType.CreateManualTransaction,
                TransactionExpRewards.CreateManualTransaction,
                now),
            ct);

        // account, budget period và gamification đã tracked (cùng DbContext) — AddAsync flush
        // mọi thay đổi atomically, xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.AddAsync(transaction, ct);
        transaction.Category = category;

        await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, command.TransactedAt, ct);
        await GamificationCache.InvalidateAsync(_cache, command.UserId, ct);

        // SAU khi AddAsync đã lưu — xem ghi chú ở ConfirmTransactionCommandHandler.
        await _budgetAlertNotifier.SendAsync(budgetAlerts, ct);

        return TransactionMapper.ToDto(transaction);
    }
}
