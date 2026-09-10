using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateManualTransactionCommandHandler : ICreateManualTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IValidator<CreateManualTransactionCommand> _validator;

    public CreateManualTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IValidator<CreateManualTransactionCommand> validator)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
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
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            MerchantName = command.MerchantName,
            Description = command.Description,
            TransactedAt = command.TransactedAt,
            BalanceAfterCents = account.BalanceCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // account đã tracked (cùng DbContext) — AddAsync flush cả 2 thay đổi atomically,
        // xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.AddAsync(transaction, ct);
        transaction.Category = category;

        return TransactionMapper.ToDto(transaction);
    }
}
