using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class ConfirmTransactionCommandHandler : IConfirmTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly IGamificationService _gamificationService;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ICacheService _cache;

    public ConfirmTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        IBudgetAlertNotifier budgetAlertNotifier,
        IGamificationService gamificationService,
        ICategoryRepository categoryRepository,
        IAIServiceClient aiServiceClient,
        ICacheService cache)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _budgetAlertNotifier = budgetAlertNotifier;
        _gamificationService = gamificationService;
        _categoryRepository = categoryRepository;
        _aiServiceClient = aiServiceClient;
        _cache = cache;
    }

    public async Task<TransactionDto> HandleAsync(ConfirmTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        if (transaction.Status != TransactionStatus.Draft)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.NotDraft,
                "Giao dịch đã được xác nhận hoặc không còn ở trạng thái nháp.");
        }

        var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);

        // Đổi danh mục TRƯỚC ApplyDeltaAsync: hạn mức phải bị trừ vào danh mục người dùng
        // CHỌN, không phải danh mục AI đoán. Đặt sau là tiền vào sai ngân sách mà không có gì
        // báo — số vẫn cộng đủ, chỉ nằm nhầm chỗ.
        var predictedSlug = transaction.Category?.Slug;
        var categoryChanged = false;

        if (command.CategoryId is { } chosenCategoryId && chosenCategoryId != transaction.CategoryId)
        {
            var category = await _categoryRepository.GetByIdAsync(chosenCategoryId, ct);
            if (category is null || (category.UserId is not null && category.UserId != command.UserId))
            {
                throw new NotFoundException("Category", chosenCategoryId);
            }

            transaction.CategoryId = category.Id;
            transaction.Category = category;
            categoryChanged = true;
        }

        var now = DateTimeOffset.UtcNow;
        account.BalanceCents += transaction.TransactionType == TransactionType.Credit
            ? transaction.AmountCents
            : -transaction.AmountCents;
        account.UpdatedAt = now;

        transaction.Status = TransactionStatus.Confirmed;
        transaction.UpdatedAt = now;

        var budgetAlerts = await _budgetPeriodService.ApplyDeltaAsync(
            command.UserId,
            transaction.CategoryId,
            TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents),
            transaction.TransactedAt,
            ct);

        await _gamificationService.RecordActivityAsync(
            new GamificationActivity(
                command.UserId,
                MissionConditionType.ConfirmTransaction,
                TransactionExpRewards.ConfirmTransaction,
                now),
            ct);

        // account, budget period và gamification đều đã được EF Core track (cùng DbContext
        // scoped với ITransactionRepository) — UpdateAsync bên dưới gọi SaveChangesAsync 1 lần
        // duy nhất, flush tất cả trong cùng 1 DB transaction ngầm định của EF Core. Đây là
        // cách đạt atomicity mà không cần thêm abstraction Unit-of-Work mới.
        await _transactionRepository.UpdateAsync(transaction, ct);

        await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);
        await GamificationCache.InvalidateAsync(_cache, command.UserId, ct);

        // SAU khi UpdateAsync đã lưu: gửi trước đó là báo cho người dùng về một giao dịch có
        // thể bị rollback (docx Flow 2 mục 2a — kiểm tra ngưỡng ngay khi giao dịch phát sinh).
        await _budgetAlertNotifier.SendAsync(budgetAlerts, ct);

        if (categoryChanged && transaction.Source == TransactionSource.Notification)
        {
            // Bảng tình huống biên của docx: "lưu lại lựa chọn của người dùng để cải thiện
            // thuật toán sau này". Đây chính là tín hiệu quý nhất — người dùng vừa sửa đúng
            // cái AI đoán sai. IAIServiceClient tự nuốt lỗi nên không cần try/catch.
            await _aiServiceClient.SendFeedbackAsync(
                new FeedbackRequest(
                    transaction.Id,
                    command.UserId,
                    null,
                    transaction.FinancialAccount?.PackageName ?? "unknown",
                    predictedSlug,
                    transaction.Category?.Slug,
                    "category_correction"),
                ct);
        }

        return TransactionMapper.ToDto(transaction);
    }
}
