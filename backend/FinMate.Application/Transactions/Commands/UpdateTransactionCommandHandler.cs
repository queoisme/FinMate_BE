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
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly ICacheService _cache;
    private readonly IValidator<UpdateTransactionCommand> _validator;

    public UpdateTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IAIServiceClient aiServiceClient,
        IBudgetPeriodService budgetPeriodService,
        ICacheService cache,
        IValidator<UpdateTransactionCommand> validator)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _aiServiceClient = aiServiceClient;
        _budgetPeriodService = budgetPeriodService;
        _cache = cache;
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
        var wasConfirmed = transaction.Status == TransactionStatus.Confirmed;
        var oldTransactedAt = transaction.TransactedAt;

        // Chỉ giao dịch đã Confirmed mới ảnh hưởng balance_cents và budget_periods — Draft
        // chưa từng cộng/trừ gì nên đổi amount/account/category ở Draft không cần revert.
        if (transaction.Status == TransactionStatus.Confirmed)
        {
            // Revert theo giá trị CŨ trước khi ghi đè entity: category, số tiền và ngày giao
            // dịch cũ có thể trỏ vào một budget khác và một chu kỳ khác với giá trị mới.
            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                transaction.CategoryId,
                -TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents),
                transaction.TransactedAt,
                ct);

            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                newCategory?.Id,
                TransactionBudgetDelta.Spend(command.TransactionType, command.AmountCents),
                command.TransactedAt,
                ct);

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

        // account(s) và budget period(s) ở trên (nếu có) đã tracked cùng DbContext —
        // UpdateAsync flush atomically, xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);
        transaction.Category = newCategory;

        if (wasConfirmed)
        {
            // Đổi ngày giao dịch có thể kéo chi tiêu sang chu kỳ khác — xóa cache cả 2 tháng.
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, oldTransactedAt, ct);
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, command.TransactedAt, ct);
        }

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
