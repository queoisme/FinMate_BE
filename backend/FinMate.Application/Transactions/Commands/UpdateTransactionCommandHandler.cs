using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class UpdateTransactionCommandHandler : IUpdateTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly IValidator<UpdateTransactionCommand> _validator;

    public UpdateTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IAIServiceClient aiServiceClient,
        IValidator<UpdateTransactionCommand> validator)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _aiServiceClient = aiServiceClient;
        _validator = validator;
    }

    public async Task<TransactionDto> HandleAsync(UpdateTransactionCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        Category? newCategory = null;
        if (command.CategoryId is not null)
        {
            newCategory = await _categoryRepository.GetByIdAsync(command.CategoryId.Value, ct);
            if (newCategory is null || (newCategory.UserId is not null && newCategory.UserId != command.UserId))
            {
                throw new NotFoundException("Category", command.CategoryId.Value);
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Chỉ giao dịch đã Confirmed mới ảnh hưởng balance_cents — Draft chưa từng cộng/trừ
        // gì nên đổi amount/account ở trạng thái Draft không cần revert.
        // TODO [!] Blocked by Phase 5: revert/áp dụng lại budget_periods theo amount/category mới.
        if (transaction.Status == TransactionStatus.Confirmed)
        {
            var oldAccount = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
            var oldDelta = transaction.TransactionType == TransactionType.Credit ? transaction.AmountCents : -transaction.AmountCents;
            oldAccount.BalanceCents -= oldDelta;
            oldAccount.UpdatedAt = now;

            var newAccount = command.FinancialAccountId == transaction.FinancialAccountId
                ? oldAccount
                : await _financialAccountRepository.GetByIdAsync(command.FinancialAccountId, command.UserId, ct)
                    ?? throw new NotFoundException("FinancialAccount", command.FinancialAccountId);
            var newDelta = command.TransactionType == TransactionType.Credit ? command.AmountCents : -command.AmountCents;
            newAccount.BalanceCents += newDelta;
            newAccount.UpdatedAt = now;

            transaction.BalanceAfterCents = newAccount.BalanceCents;
        }

        var categoryChanged = transaction.CategoryId != (newCategory?.Id);
        var oldCategorySlug = transaction.Category?.Slug;

        transaction.FinancialAccountId = command.FinancialAccountId;
        transaction.CategoryId = newCategory?.Id;
        transaction.AmountCents = command.AmountCents;
        transaction.TransactionType = command.TransactionType;
        transaction.TransactedAt = command.TransactedAt;
        transaction.MerchantName = command.MerchantName;
        transaction.Description = command.Description;
        transaction.UpdatedAt = now;

        // account(s) ở trên (nếu có) đã tracked cùng DbContext — UpdateAsync flush atomically,
        // xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);
        transaction.Category = newCategory;

        if (categoryChanged && transaction.Source == TransactionSource.Notification)
        {
            // IAIServiceClient.SendFeedbackAsync tự nuốt lỗi (best-effort) — không cần try/catch ở đây.
            await _aiServiceClient.SendFeedbackAsync(
                new FeedbackRequest(
                    transaction.Id,
                    command.UserId,
                    null,
                    transaction.FinancialAccount?.PackageName ?? "unknown",
                    oldCategorySlug,
                    newCategory?.Slug,
                    "category_correction"),
                ct);
        }

        return TransactionMapper.ToDto(transaction);
    }
}
