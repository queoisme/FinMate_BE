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

        // Transfer chạm 2 số dư và không tiêu ngân sách; debit/credit chạm 1 và có tiêu. Cho
        // phép đổi qua lại giữa 2 thế giới đó nghĩa là phải revert theo một hình dạng rồi áp
        // theo hình dạng khác — nhiều nhánh, dễ lệch số dư, mà không giải quyết nhu cầu thật
        // nào (user ghi nhầm loại thì xóa và tạo lại). Chặn thẳng và nói rõ lý do.
        if ((transaction.TransactionType == TransactionType.Transfer)
            != (command.TransactionType == TransactionType.Transfer))
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.TypeChangeNotAllowed,
                "Không thể đổi giao dịch thường thành chuyển khoản nội bộ hoặc ngược lại. "
                + "Vui lòng xóa và tạo lại.");
        }

        if (command.TransactionType == TransactionType.Transfer
            && command.CounterAccountId == command.FinancialAccountId)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.TransferSameAccount,
                "Ví nguồn và ví đích phải khác nhau.");
        }

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

            // Revert hình dạng CŨ rồi áp hình dạng MỚI. Với transfer, "hình dạng" gồm 2 ví —
            // ví đích cũng phải được trả về trạng thái cũ trước khi cộng theo giá trị mới.
            var accounts = new Dictionary<Guid, FinancialAccount>();

            await ApplyBalanceAsync(accounts, command.UserId, transaction.FinancialAccountId,
                -SourceDelta(transaction.TransactionType, transaction.AmountCents), now, ct);
            if (transaction.CounterAccountId is not null)
            {
                await ApplyBalanceAsync(accounts, command.UserId, transaction.CounterAccountId.Value,
                    -transaction.AmountCents, now, ct);
            }

            await ApplyBalanceAsync(accounts, command.UserId, command.FinancialAccountId,
                SourceDelta(command.TransactionType, command.AmountCents), now, ct);
            if (command.CounterAccountId is not null)
            {
                await ApplyBalanceAsync(accounts, command.UserId, command.CounterAccountId.Value,
                    command.AmountCents, now, ct);
            }

            // Luôn là số dư ví NGUỒN, thống nhất với lúc tạo.
            transaction.BalanceAfterCents = accounts[command.FinancialAccountId].BalanceCents;
        }

        var categoryChanged = transaction.CategoryId != (newCategory?.Id);
        var oldCategorySlug = transaction.Category?.Slug;

        transaction.FinancialAccountId = command.FinancialAccountId;
        transaction.CounterAccountId = command.CounterAccountId;
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

    /// <summary>
    /// Tác động của giao dịch lên số dư ví NGUỒN. Credit là tiền vào (+); Debit và Transfer
    /// đều là tiền rời ví nguồn (−) — với Transfer thì phần (+) nằm ở ví đích, xử lý riêng.
    /// </summary>
    private static long SourceDelta(TransactionType type, long amountCents)
        => type == TransactionType.Credit ? amountCents : -amountCents;

    /// <summary>
    /// Cộng dồn delta vào ví, nhớ lại ví đã nạp trong lượt này. Một lượt update có thể chạm
    /// cùng một ví nhiều lần (ví cũ trùng ví mới, hoặc ví đích cũ trùng ví nguồn mới) — phải
    /// cộng dồn trên cùng một instance thì kết quả mới đúng.
    /// </summary>
    private async Task ApplyBalanceAsync(
        Dictionary<Guid, FinancialAccount> loaded,
        Guid userId,
        Guid accountId,
        long deltaCents,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!loaded.TryGetValue(accountId, out var account))
        {
            account = await _financialAccountRepository.GetByIdAsync(accountId, userId, ct)
                ?? throw new NotFoundException("FinancialAccount", accountId);
            loaded[accountId] = account;
        }

        account.BalanceCents += deltaCents;
        account.UpdatedAt = now;
    }
}
