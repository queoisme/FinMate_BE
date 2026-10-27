using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Application.Gamification;
using FinMate.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Transactions.Commands;

public class CreateManualTransactionCommandHandler : ICreateManualTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly IGamificationService _gamificationService;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly IReceiptScanRepository _receiptScanRepository;
    private readonly ICacheService _cache;
    private readonly ILogger<CreateManualTransactionCommandHandler> _logger;
    private readonly IValidator<CreateManualTransactionCommand> _validator;
    private readonly ITrainingSampleService? _trainingSamples;  // null: không có dịch vụ thu mẫu (test cũ) thì bỏ qua

    public CreateManualTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IBudgetPeriodService budgetPeriodService,
        IBudgetAlertNotifier budgetAlertNotifier,
        IGamificationService gamificationService,
        IAIServiceClient aiServiceClient,
        IReceiptScanRepository receiptScanRepository,
        ICacheService cache,
        ILogger<CreateManualTransactionCommandHandler> logger,
        IValidator<CreateManualTransactionCommand> validator,
        ITrainingSampleService? trainingSamples = null)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _budgetPeriodService = budgetPeriodService;
        _budgetAlertNotifier = budgetAlertNotifier;
        _gamificationService = gamificationService;
        _aiServiceClient = aiServiceClient;
        _receiptScanRepository = receiptScanRepository;
        _cache = cache;
        _logger = logger;
        _validator = validator;
        _trainingSamples = trainingSamples;
    }

    public async Task<TransactionCreationResult> HandleAsync(CreateManualTransactionCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Gửi lại thì trả về chính giao dịch cũ, không tạo thêm. Đây là chốt chặn cho hàng
        // đợi offline ở docx mục "Mạng Offline" — xem Transaction.ClientRequestId.
        if (command.ClientRequestId is { } clientRequestId)
        {
            var existing = await _transactionRepository.GetByClientRequestIdAsync(
                command.UserId, clientRequestId, ct);
            if (existing is not null)
            {
                return new TransactionCreationResult(TransactionMapper.ToDto(existing), AlreadyExisted: true);
            }
        }

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
            ClientRequestId = command.ClientRequestId,
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
            TransactionBudgetDelta.Spend(command.TransactionType, command.AmountCents, category),
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

        // Giao dịch điền từ ảnh hóa đơn: nối nó với bản ghi quét. Kết quả bị bỏ qua có chủ ý (xem ReceiptScanId ở command): mã sai không được làm hỏng việc lưu tiền.
        var scanWasSplit = false;
        if (command.ReceiptScanId is { } scanId && command.Source == TransactionSource.Receipt)
        {
            // Giao dịch đã lưu rồi: nối/kiểm tra lỗi (mất kết nối, v.v.) không được biến thành 500 để client bấm Lưu lại và tạo trùng.
            try
            {
                scanWasSplit = await _receiptScanRepository.IsSplitAsync(scanId, command.UserId, ct);
                string group;
                if (scanWasSplit)
                {
                    // receiptGroup do client gửi chỉ là khóa nối: rỗng hoặc quá 100 ký tự (cột giới hạn 100, vượt là SQL ném), hoặc có ký tự điều khiển (Postgres từ chối NUL) thì dùng slug danh mục đã lưu.
                    group = command.ReceiptGroup?.Trim() ?? string.Empty;
                    if (group.Length == 0 || group.Length > 100 || group.Any(char.IsControl))
                    {
                        group = category?.Slug ?? ReceiptScanLink.UnknownGroup;
                    }
                }
                else
                {
                    // Lần quét không tách: một khóa cố định (slug AI đoán cho cả bill), bỏ qua receiptGroup và danh mục người dùng chọn,
                    // để lưu cùng lần quét hai lần với hai danh mục khác nhau vẫn chỉ nối một giao dịch (phép đo độ chính xác không đếm đôi).
                    var scan = await _receiptScanRepository.GetByIdAsync(scanId, command.UserId, ct);
                    group = scan?.CategorySlug ?? ReceiptScanLink.UnknownGroup;
                }

                await _receiptScanRepository.LinkToTransactionAsync(scanId, command.UserId, transaction.Id, group, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Chỉ ghi tên loại lỗi (không ghi message: có thể chứa dữ liệu). Không biết có tách hay không thì KHÔNG học: bỏ lỡ một bài vô hại, dạy sai thì kéo dài.
                scanWasSplit = true;
                _logger.LogWarning("Receipt scan link/split check failed after the transaction was saved: {ExceptionType}", ex.GetType().Name);
            }
        }

        // Nhãn cuối cho vòng tự học: chỉ khi giao dịch nối với lần quét MỘT nhóm (scanWasSplit = false; lỗi kiểm tra thì đã coi là tách). Dịch vụ tự kiểm cờ đồng ý.
        if (_trainingSamples is not null && command.ReceiptScanId is { } labelledScan && command.Source == TransactionSource.Receipt && !scanWasSplit)
        {
            await _trainingSamples.LabelAsync(command.UserId, labelledScan, category, ct);
        }

        // Người dùng đã chọn danh mục cho cửa hàng trên một hóa đơn: nhờ AI Service nhớ cho lần quét sau (best-effort, sau khi đã lưu).
        if (ReceiptMemory.ShouldLearn(command.Source, category, command.MerchantName, scanWasSplit))
        {
            await _aiServiceClient.LearnMerchantAsync(
                new LearnMerchantRequest(command.UserId, command.MerchantName!, category!.Slug), ct);
        }

        return new TransactionCreationResult(TransactionMapper.ToDto(transaction), AlreadyExisted: false);
    }
}
