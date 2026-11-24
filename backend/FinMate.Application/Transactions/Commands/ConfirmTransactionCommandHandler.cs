using FinMate.Application.Groups;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class ConfirmTransactionCommandHandler : IConfirmTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ICacheService _cache;
    private readonly IForecastAlertTrigger? _forecastAlerts;

    private readonly IGroupLedger? _groupLedger;
    private readonly Refunds.IRefundLedger? _refunds;
    private readonly IDailySummaryRefresher? _summaries;

    // §16.5: ghi nhớ danh mục người dùng TỰ chọn cho một merchant, để lần sau AI/QR gợi ý đúng
    // luôn — null: không ghi override (test cũ).
    private readonly IMerchantCategoryOverrideRepository? _merchantCategoryOverrides;

    private readonly IAiLearningQueue? _learning;  // null (test cũ): gọi thẳng AI Service như trước

    // Hộp thư tab "Biến động": giao dịch bắt từ thông báo ngân hàng chỉ vào đây khi được xác nhận. null (test cũ): bỏ qua.
    private readonly Notifications.IUserNotificationService? _userNotifications;

    public ConfirmTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        IBudgetAlertNotifier budgetAlertNotifier,
        ICategoryRepository categoryRepository,
        IAIServiceClient aiServiceClient,
        ICacheService cache,
        IForecastAlertTrigger? forecastAlerts = null,
        IGroupLedger? groupLedger = null,
        Refunds.IRefundLedger? refunds = null,
        IDailySummaryRefresher? summaries = null,
        IMerchantCategoryOverrideRepository? merchantCategoryOverrides = null,
        IAiLearningQueue? learning = null,
        Notifications.IUserNotificationService? userNotifications = null)
    {
        _learning = learning;
        _userNotifications = userNotifications;
        _groupLedger = groupLedger;
        _refunds = refunds;
        _summaries = summaries;
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _budgetAlertNotifier = budgetAlertNotifier;
        _categoryRepository = categoryRepository;
        _aiServiceClient = aiServiceClient;
        _cache = cache;
        _forecastAlerts = forecastAlerts;
        _merchantCategoryOverrides = merchantCategoryOverrides;
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

        // Phase 81: nháp là khoản hoàn/đảo (người dùng vừa chọn khoản gốc, hoặc đã đặt bằng PUT /related) → đường riêng, dưới khoá khoản gốc.
        if (_refunds is not null && (command.RelatedTransactionId ?? transaction.RelatedTransactionId) is { } originalId)
        {
            return await ConfirmAsRefundAsync(command, transaction, originalId, _refunds, ct);
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
        await ApplyToBalanceAsync(
            account, transaction, transaction.TransactionType == TransactionType.Credit ? transaction.AmountCents : -transaction.AmountCents, ct);
        account.UpdatedAt = now;

        transaction.Status = TransactionStatus.Confirmed;
        transaction.UpdatedAt = now;

        var budgetAlerts = await _budgetPeriodService.ApplyDeltaAsync(
            command.UserId,
            transaction.CategoryId,
            TransactionBudgetDelta.Spend(transaction, transaction.Category),
            transaction.TransactedAt,
            ct);

        // account và budget period đều đã được EF Core track (cùng DbContext
        // scoped với ITransactionRepository) — UpdateAsync bên dưới gọi SaveChangesAsync 1 lần
        // duy nhất, flush tất cả trong cùng 1 DB transaction ngầm định của EF Core. Đây là
        // cách đạt atomicity mà không cần thêm abstraction Unit-of-Work mới.
        IReadOnlyList<GroupBudgetAlert> groupAlerts = [];
        if (_groupLedger is not null
            && await _groupLedger.ResolveAsync(command.UserId, command.GroupId, command.GroupCategoryId, transaction, transaction.Category, ct) is { } target)
        {
            _groupLedger.MarkShared(transaction, target, command.UserId, now);
            groupAlerts = await _groupLedger.ApplyAsync(transaction, transaction.Category, +1, ct);
        }

        await _transactionRepository.UpdateAsync(transaction, ct);

        await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);

        // SAU khi UpdateAsync đã lưu: gửi trước đó là báo cho người dùng về một giao dịch có
        // thể bị rollback (docx Flow 2 mục 2a — kiểm tra ngưỡng ngay khi giao dịch phát sinh).
        await _budgetAlertNotifier.SendAsync(budgetAlerts, ct);
        if (_groupLedger is not null)
        {
            await _groupLedger.SendAlertsAsync(groupAlerts, ct);
        }
        await BalanceChangeInbox.WriteAsync(_userNotifications, transaction, ct);

        if (categoryChanged && transaction.Source == TransactionSource.Notification)
        {
            // Bảng tình huống biên của docx: "lưu lại lựa chọn của người dùng để cải thiện
            // thuật toán sau này". Đây chính là tín hiệu quý nhất — người dùng vừa sửa đúng
            // cái AI đoán sai. IAIServiceClient tự nuốt lỗi nên không cần try/catch.
            var feedback = new FeedbackRequest(
                transaction.Id,
                command.UserId,
                null,
                transaction.FinancialAccount?.PackageName ?? "unknown",
                predictedSlug,
                transaction.Category?.Slug,
                "category_correction");
            if (_learning is not null)
            {
                _learning.Feedback(feedback);
            }
            else
            {
                await _aiServiceClient.SendFeedbackAsync(feedback, ct);
            }

            // §7 Mapping-trigger rule + §16.5: chỉ ghi override khi user TỰ chọn danh mục, không
            // ghi khi chỉ chấp nhận gợi ý AI nguyên vẹn (categoryChanged đã đảm bảo đúng điều này).
            if (_merchantCategoryOverrides is not null && !string.IsNullOrWhiteSpace(transaction.MerchantName))
            {
                var key = MerchantCategoryKey.Normalize(transaction.MerchantName);
                var existingOverride = await _merchantCategoryOverrides.GetAsync(command.UserId, key, ct);
                if (existingOverride is null)
                {
                    await _merchantCategoryOverrides.AddAsync(new MerchantCategoryOverride
                    {
                        UserId = command.UserId,
                        MerchantKey = key,
                        DisplayName = transaction.MerchantName,
                        CategoryId = transaction.CategoryId!.Value,
                        CreatedAt = now,
                        UpdatedAt = now,
                    }, ct);
                }
                else
                {
                    existingOverride.CategoryId = transaction.CategoryId!.Value;
                    existingOverride.UpdatedAt = now;
                    await _merchantCategoryOverrides.UpdateAsync(existingOverride, ct);
                }
            }
        }
        await NotifyForecastAsync(command.UserId, ct);

        return TransactionMapper.ToDto(transaction);
    }

    private async Task<TransactionDto> ConfirmAsRefundAsync(
        ConfirmTransactionCommand command, Domain.Entities.Transaction transaction, Guid originalId, Refunds.IRefundLedger refunds, CancellationToken ct)
    {
        var relation = command.Relation ?? transaction.Relation ?? TransactionRelation.Refund;
        var effects = await _transactionRepository.RunWithOriginalLockedAsync(command.UserId, originalId, async original =>
        {
            // D13: danh mục của khoản hoàn là danh mục khoản gốc; người dùng chọn danh mục khác ở bước này là mâu thuẫn, báo rõ thay vì lặng lẽ bỏ qua.
            if (command.CategoryId is { } chosen && chosen != original.CategoryId)
            {
                throw new BusinessRuleException(TransactionErrorCodes.RefundCategoryLocked, "Khoản hoàn dùng danh mục của khoản chi gốc.");
            }

            var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);

            var now = DateTimeOffset.UtcNow;
            transaction.Status = TransactionStatus.Confirmed;
            transaction.UpdatedAt = now;
            var result = await refunds.AttachAsync(command.UserId, original, transaction, relation, before: null, ct);

            await ApplyToBalanceAsync(account, transaction, transaction.AmountCents, ct);   // AttachAsync đã bảo đảm đây là tiền vào
            account.UpdatedAt = now;
            await _transactionRepository.UpdateAsync(transaction, ct);
            return result;
        }, ct);

        await new RefundAfterSave(_budgetAlertNotifier, _cache, _groupLedger, _summaries, _forecastAlerts).RunAsync(command.UserId, effects, ct);
        await BalanceChangeInbox.WriteAsync(_userNotifications, transaction, ct);
        return TransactionMapper.ToDto(transaction);
    }

    /// <summary>
    /// Số dư ví sau khi xác nhận. Thông báo ngân hàng đã ghi sẵn số dư thật: nếu đó là giao dịch MỚI NHẤT của ví thì lấy luôn số ngân hàng báo, tự sửa mọi sai lệch tích lũy
    /// (giao dịch bỏ sót, số dư ban đầu nhập sai). Còn giao dịch mới hơn trong ví thì số ngân hàng đã cũ — chỉ cộng/trừ như thường, kẻo mất các giao dịch đó.
    /// </summary>
    private async Task ApplyToBalanceAsync(FinancialAccount account, Transaction transaction, long deltaCents, CancellationToken ct)
    {
        if (transaction.Source == TransactionSource.Notification
            && transaction.BalanceAfterCents is >= 0 and var bankBalance
            && !await _transactionRepository.HasConfirmedAfterAsync(account.Id, transaction.TransactedAt, transaction.Id, ct))
        {
            account.BalanceCents = bankBalance;
            return;
        }

        account.BalanceCents += deltaCents;
    }

    /// <summary>Kiểm tra số dư thấp / vượt ngân sách sớm ngoài request (Flow 6). Gọi SAU khi đã lưu; trigger tự nuốt lỗi.</summary>
    private Task NotifyForecastAsync(Guid userId, CancellationToken ct)
        => _forecastAlerts is null ? Task.CompletedTask : _forecastAlerts.UserChangedAsync(userId, ct);
}
