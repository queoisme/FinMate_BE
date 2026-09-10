using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
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

    public RetryFailedNotificationJob(
        INotificationLogRepository notificationLogRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAIServiceClient aiServiceClient)
    {
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
                await _notificationLogRepository.AddAiResultAsync(aiResult, ct);

                if (pipelineResult == PipelineResult.Financial
                    && !aiResult.IsPotentialDuplicate
                    && aiResult.AmountCents is > 0
                    && aiResult.TransactionType is not null
                    && log.FinancialAccountId is not null)
                {
                    var category = aiResult.CategorySlug is null
                        ? null
                        : await _categoryRepository.GetSystemBySlugAsync(aiResult.CategorySlug, ct);

                    var now = DateTimeOffset.UtcNow;
                    await _transactionRepository.AddAsync(new Transaction
                    {
                        UserId = log.UserId,
                        FinancialAccountId = log.FinancialAccountId.Value,
                        CategoryId = category?.Id,
                        NotificationLogId = log.Id,
                        AmountCents = aiResult.AmountCents.Value,
                        TransactionType = aiResult.TransactionType.Value,
                        Source = TransactionSource.Notification,
                        Status = TransactionStatus.Draft,
                        MerchantName = aiResult.MerchantName,
                        Description = aiResult.Description,
                        TransactedAt = aiResult.TransactedAt ?? log.ReceivedAt,
                        BalanceAfterCents = aiResult.BalanceAfterCents,
                        CreatedAt = now,
                        UpdatedAt = now,
                    }, ct);
                }

                log.Status = NotificationLogStatus.Processed;
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
