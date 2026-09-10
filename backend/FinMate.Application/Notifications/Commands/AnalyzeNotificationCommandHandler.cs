using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Notifications.Commands;

public class AnalyzeNotificationCommandHandler : IAnalyzeNotificationCommandHandler
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IValidator<AnalyzeNotificationCommand> _validator;

    public AnalyzeNotificationCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        INotificationLogRepository notificationLogRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAIServiceClient aiServiceClient,
        IPushNotificationService pushNotificationService,
        IValidator<AnalyzeNotificationCommand> validator)
    {
        _financialAccountRepository = financialAccountRepository;
        _notificationLogRepository = notificationLogRepository;
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
        _aiServiceClient = aiServiceClient;
        _pushNotificationService = pushNotificationService;
        _validator = validator;
    }

    public async Task<NotificationAnalysisResultDto> HandleAsync(AnalyzeNotificationCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var now = DateTimeOffset.UtcNow;
        var contentHash = ComputeContentHash(command);

        var account = await _financialAccountRepository.GetByUserAndMonitoredPackageAsync(command.UserId, command.PackageName, ct);
        if (account is null)
        {
            var ignoredLog = new NotificationLog
            {
                UserId = command.UserId,
                FinancialAccountId = null,
                PackageName = command.PackageName,
                NotificationTitle = command.NotificationTitle,
                NotificationBody = command.NotificationBody,
                ContentHash = contentHash,
                ReceivedAt = command.ReceivedAt,
                Status = NotificationLogStatus.Ignored,
                ProcessedAt = now,
                CreatedAt = now,
            };
            await _notificationLogRepository.AddAsync(ignoredLog, ct);
            return new NotificationAnalysisResultDto(ignoredLog.Id, "Ignored", null);
        }

        var existing = await _notificationLogRepository.GetRecentByContentHashAsync(
            command.UserId, contentHash, now - DedupWindow, ct);
        if (existing is not null)
        {
            return new NotificationAnalysisResultDto(existing.Id, existing.Status.ToString(), null);
        }

        var log = new NotificationLog
        {
            UserId = command.UserId,
            FinancialAccountId = account.Id,
            PackageName = command.PackageName,
            NotificationTitle = command.NotificationTitle,
            NotificationBody = command.NotificationBody,
            ContentHash = contentHash,
            ReceivedAt = command.ReceivedAt,
            Status = NotificationLogStatus.Pending,
            CreatedAt = now,
        };
        await _notificationLogRepository.AddAsync(log, ct);

        AnalyzeResponse response;
        try
        {
            response = await _aiServiceClient.AnalyzeAsync(
                new AnalyzeRequest(log.Id, command.UserId, command.PackageName, command.NotificationTitle, command.NotificationBody, command.ReceivedAt),
                ct);
        }
        catch (AIServiceUnavailableException ex)
        {
            log.Status = NotificationLogStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _notificationLogRepository.UpdateAsync(log, ct);
            throw;
        }

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
            CreatedAt = now,
        };
        await _notificationLogRepository.AddAiResultAsync(aiResult, ct);

        log.Status = NotificationLogStatus.Processed;
        log.ProcessedAt = DateTimeOffset.UtcNow;
        await _notificationLogRepository.UpdateAsync(log, ct);

        Guid? draftTransactionId = null;
        if (pipelineResult == PipelineResult.Financial
            && !aiResult.IsPotentialDuplicate
            && aiResult.AmountCents is > 0
            && aiResult.TransactionType is not null)
        {
            Category? category = aiResult.CategorySlug is null
                ? null
                : await _categoryRepository.GetSystemBySlugAsync(aiResult.CategorySlug, ct);

            var transaction = new Transaction
            {
                UserId = command.UserId,
                FinancialAccountId = account.Id,
                CategoryId = category?.Id,
                NotificationLogId = log.Id,
                AmountCents = aiResult.AmountCents.Value,
                TransactionType = aiResult.TransactionType.Value,
                Source = TransactionSource.Notification,
                Status = TransactionStatus.Draft,
                MerchantName = aiResult.MerchantName,
                Description = aiResult.Description,
                TransactedAt = aiResult.TransactedAt ?? command.ReceivedAt,
                BalanceAfterCents = aiResult.BalanceAfterCents,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _transactionRepository.AddAsync(transaction, ct);
            draftTransactionId = transaction.Id;

            await _pushNotificationService.NotifyAsync(
                command.UserId,
                "Giao dịch mới cần xác nhận",
                $"{transaction.MerchantName ?? "Giao dịch"}: {transaction.AmountCents:N0}đ",
                ct);
        }

        return new NotificationAnalysisResultDto(log.Id, log.Status.ToString(), draftTransactionId);
    }

    private static string ComputeContentHash(AnalyzeNotificationCommand command)
    {
        var roundedMinute = new DateTimeOffset(
            command.ReceivedAt.Year, command.ReceivedAt.Month, command.ReceivedAt.Day,
            command.ReceivedAt.Hour, command.ReceivedAt.Minute, 0, command.ReceivedAt.Offset);
        var raw = $"{command.PackageName}|{command.NotificationTitle}|{command.NotificationBody}|{roundedMinute:O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
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
