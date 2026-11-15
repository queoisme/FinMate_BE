using FinMate.Application.Groups;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class ConfirmTransactionCommandHandler : IConfirmTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ITransactionConfirmationEffects _confirmationEffects;
    private readonly ISystemConfigProvider _config;
    private readonly IMerchantCategoryOverrideRepository _merchantCategoryOverrides;
    private readonly IForecastAlertTrigger? _forecastAlerts;

    private readonly IGroupLedger? _groupLedger;

    public ConfirmTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IAIServiceClient aiServiceClient,
        ITransactionConfirmationEffects confirmationEffects,
        ISystemConfigProvider config,
        IMerchantCategoryOverrideRepository merchantCategoryOverrides,
        IForecastAlertTrigger? forecastAlerts = null,
        IGroupLedger? groupLedger = null)
    {
        _groupLedger = groupLedger;
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _aiServiceClient = aiServiceClient;
        _confirmationEffects = confirmationEffects;
        _config = config;
        _merchantCategoryOverrides = merchantCategoryOverrides;
        _forecastAlerts = forecastAlerts;
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

        // §8: trùng nghiệp vụ có thể xuất hiện SAU khi Draft được tạo (vd user nhập tay 1 giao
        // dịch trùng trong lúc Draft còn chờ xác nhận) — phải chặn ở đây lại, không được âm thầm
        // bỏ qua hay tự ý commit: trả lỗi để client quyết định.
        var pipelineSettings = (await _config.GetAsync(ct)).Document.NotificationPipeline;
        var isDuplicate = await _transactionRepository.HasAuthoritativeDuplicateAsync(
            transaction.FinancialAccountId,
            transaction.AmountCents,
            transaction.TransactionType,
            transaction.TransactedAt,
            TimeSpan.FromMinutes(pipelineSettings.BusinessDuplicateWindowMinutes),
            transaction.Id,
            ct);
        if (isDuplicate)
        {
            throw new ConflictException(
                TransactionErrorCodes.PossibleDuplicateOnConfirm,
                "Giao dịch này trùng với một giao dịch khác đã có trong ví. Vui lòng kiểm tra lại trước khi xác nhận.");
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

        // Balance + budget: logic dùng chung với nhánh Auto-commit của
        // AnalyzeNotificationCommandHandler (§7/§16.1), xem ITransactionConfirmationEffects.
        // KHÔNG gọi SaveChangesAsync ở đây — chỉ mutate, lưu 1 lần duy nhất ở UpdateAsync bên dưới.
        var budgetAlerts = await _confirmationEffects.ApplyAsync(transaction, account, ct);

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

        // SAU khi UpdateAsync đã lưu: gửi trước đó là báo cho người dùng về một giao dịch có
        // thể bị rollback (docx Flow 2 mục 2a — kiểm tra ngưỡng ngay khi giao dịch phát sinh).
        await _confirmationEffects.NotifyAsync(transaction, budgetAlerts, ct);
        if (_groupLedger is not null)
        {
            await _groupLedger.SendAlertsAsync(groupAlerts, ct);
        }

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

            // §7 Mapping-trigger rule + §16.5: chỉ ghi override khi user TỰ chọn danh mục, không
            // ghi khi chỉ chấp nhận gợi ý AI nguyên vẹn (categoryChanged đã đảm bảo đúng điều này).
            if (!string.IsNullOrWhiteSpace(transaction.MerchantName))
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

    /// <summary>Kiểm tra số dư thấp / vượt ngân sách sớm ngoài request (Flow 6). Gọi SAU khi đã lưu; trigger tự nuốt lỗi.</summary>
    private Task NotifyForecastAsync(Guid userId, CancellationToken ct)
        => _forecastAlerts is null ? Task.CompletedTask : _forecastAlerts.UserChangedAsync(userId, ct);
}
