using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Retries notification_logs stuck in Failed (AI Service was unavailable) up to 3 times.
/// </summary>
public class RetryFailedNotificationJob
{
    private const int MaxRetryCount = 3;

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly IDuplicateDetector? _duplicates;  // null: không so trùng (test cũ)

    public RetryFailedNotificationJob(
        INotificationLogRepository notificationLogRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAIServiceClient aiServiceClient,
        IDuplicateDetector? duplicates = null)
    {
        _duplicates = duplicates;
        _notificationLogRepository = notificationLogRepository;
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
        _aiServiceClient = aiServiceClient;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var failed = await _notificationLogRepository.GetFailedForRetryAsync(MaxRetryCount, ct);

        foreach (var log in failed)
        {
            log.RetryCount++;

            try
            {
                var response = await _aiServiceClient.AnalyzeAsync(
                    new AnalyzeRequest(log.Id, log.UserId, log.PackageName, log.NotificationTitle, log.NotificationBody ?? string.Empty, log.ReceivedAt),
                    ct);

                var pipelineResult = ParsePipelineResult(response.PipelineResult);
                var aiResult = new AiResult
                {
                    NotificationLogId = log.Id,
                    PipelineResult = pipelineResult,
                    ClassifierLabel = response.Classifier.Label,
                    ClassifierConfidence = response.Classifier.Confidence,
                    AmountCents = response.Extraction?.AmountCents,
                    TransactionType = ParseTransactionType(response.Extraction?.TransactionType),
                    MerchantName = response.Extraction?.MerchantName,
                    Description = response.Extraction?.Description,
                    TransactedAt = response.Extraction?.TransactedAt,
                    BalanceAfterCents = response.Extraction?.BalanceAfterCents,
                    ExtractionConfidence = response.Extraction?.Confidence,
                    CategorySlug = response.Categorization?.CategorySlug,
                    CategorizationConfidence = response.Categorization?.Confidence,
                    IsPotentialDuplicate = response.Duplicate?.IsPotentialDuplicate ?? false,
                    DuplicateRequestId = response.Duplicate?.DuplicateRequestId,
                    ClassifierVersion = response.ModelVersions?.Classifier,
                    ExtractorVersion = response.ModelVersions?.Extractor,
                    CategorizerVersion = response.ModelVersions?.Categorizer,
                    ProcessingMs = response.ProcessingMs,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                // Phase 81/83: như đường chính; job không gửi push, nháp dựng gợi ý khi app đọc nó.
                (aiResult.RelationHint, aiResult.TransferKind) = Application.Notifications.NotificationHints.Parse(response.Extraction, aiResult.TransactionType);
                await _notificationLogRepository.AddAiResultAsync(aiResult, ct);

                // Phase 82: như đường chính, cờ trùng của AI không còn làm mất thông báo; job không gửi push, nháp nghi trùng chờ trong tab Chờ duyệt.
                var duplicateOf = pipelineResult == PipelineResult.Financial
                    && aiResult.AmountCents is > 0
                    && aiResult.TransactionType is not null
                    && log.FinancialAccountId is not null
                        ? await CreateDraftAsync(log, aiResult, ct)
                        : null;

                log.Status = duplicateOf is null ? NotificationLogStatus.Processed : NotificationLogStatus.Duplicate;
                log.ProcessedAt = DateTimeOffset.UtcNow;
                log.ErrorMessage = null;
            }
            catch (AIServiceUnavailableException ex)
            {
                log.ErrorMessage = ex.Message;
            }

            await _notificationLogRepository.UpdateAsync(log, ct);
        }
    }

    /// <summary>Tạo nháp; trả id giao dịch đã ghi chính sự kiện này khi thông báo là bản gửi lại (BR-DUP-05), khi đó không tạo gì.</summary>
    private async Task<Guid?> CreateDraftAsync(NotificationLog log, AiResult aiResult, CancellationToken ct)
    {
        var category = aiResult.CategorySlug is null
            ? null
            : await _categoryRepository.GetSystemBySlugAsync(aiResult.CategorySlug, ct);

        var now = DateTimeOffset.UtcNow;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = log.UserId,
            FinancialAccountId = log.FinancialAccountId!.Value,
            CategoryId = category?.Id,
            NotificationLogId = log.Id,
            AmountCents = aiResult.AmountCents!.Value,
            TransactionType = aiResult.TransactionType!.Value,
            Source = TransactionSource.Notification,
            Status = TransactionStatus.Draft,
            MerchantName = aiResult.MerchantName,
            Description = aiResult.Description,
            TransactedAt = aiResult.TransactedAt ?? log.ReceivedAt,
            BalanceAfterCents = aiResult.BalanceAfterCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (_duplicates is not null
            && (await _duplicates.MarkSuspectedAsync(transaction, log.PackageName, aiResult.DuplicateRequestId, ct)).SameEventId is { } sameEvent)
        {
            return sameEvent;
        }

        await _transactionRepository.AddAsync(transaction, ct);
        return null;
    }

    private static PipelineResult ParsePipelineResult(string value) => value switch
    {
        "financial" => PipelineResult.Financial,
        "non_financial" => PipelineResult.NonFinancial,
        "uncertain" => PipelineResult.Uncertain,
        "extraction_failed" => PipelineResult.ExtractionFailed,
        _ => PipelineResult.Error,
    };

    private static TransactionType? ParseTransactionType(string? value) => value?.ToLowerInvariant() switch
    {
        "debit" => TransactionType.Debit,
        "credit" => TransactionType.Credit,
        _ => null,
    };
}
