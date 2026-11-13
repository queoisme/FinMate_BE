using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications;
using FinMate.Application.QrTransfers;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Notifications.Commands;

public class AnalyzeNotificationCommandHandler : IAnalyzeNotificationCommandHandler
{
    /// <summary>
    /// Chỉ còn dùng cho log <c>Ignored</c> — xem ghi chú ở
    /// <see cref="INotificationLogRepository.GetRecentIgnoredByContentHashAsync"/>.
    /// </summary>
    private static readonly TimeSpan IgnoredDedupWindow = TimeSpan.FromMinutes(5);

    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IValidator<AnalyzeNotificationCommand> _validator;
    private readonly IAiConsentReader? _consent;  // null: không có bộ đọc đồng ý thì không gửi gì ra bên thứ ba
    private readonly IQrTransferLinker? _qrLinker;  // null: không có luồng quét QR thì giao dịch chỉ dựa vào AI như trước
    private readonly Transactions.Refunds.IRefundSuggestions? _refundSuggestions;  // null: không gợi ý khoản chi gốc (test cũ)
    private readonly IDuplicateDetector? _duplicates;  // null: không so trùng (test cũ)

    public AnalyzeNotificationCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        INotificationLogRepository notificationLogRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAIServiceClient aiServiceClient,
        IPushNotificationService pushNotificationService,
        IValidator<AnalyzeNotificationCommand> validator,
        IAiConsentReader? consent = null,
        IQrTransferLinker? qrLinker = null,
        Transactions.Refunds.IRefundSuggestions? refundSuggestions = null,
        IDuplicateDetector? duplicates = null)
    {
        _refundSuggestions = refundSuggestions;
        _duplicates = duplicates;
        _financialAccountRepository = financialAccountRepository;
        _notificationLogRepository = notificationLogRepository;
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
        _aiServiceClient = aiServiceClient;
        _pushNotificationService = pushNotificationService;
        _validator = validator;
        _consent = consent;
        _qrLinker = qrLinker;
    }

    public async Task<NotificationAnalysisResultDto> HandleAsync(AnalyzeNotificationCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var now = DateTimeOffset.UtcNow;
        var contentHash = ComputeContentHash(command);

        // Kiểm tra trùng TRƯỚC khi tra ví, để cả nhánh Ignored cũng được bảo vệ.
        //
        // Đây là chốt chặn cho docx mục "Mạng Offline": app xếp thông báo vào Room rồi đồng bộ
        // khi có mạng trở lại, và một hàng đợi như thế chắc chắn sẽ gửi lại — timeout giữa
        // chừng, app bị kill, hoặc backoff. Không giới hạn thời gian ở đây: hash đã gói sẵn
        // ReceivedAt làm tròn phút nên trùng hash là trùng đúng thông báo đó.
        var processed = await _notificationLogRepository.GetProcessedByContentHashAsync(
            command.UserId, contentHash, ct);
        if (processed is not null)
        {
            // Lần xử lý trước có thể đã tạo giao dịch nháp — client gửi lại (đồng bộ hàng đợi offline) vẫn cần biết
            // giao dịch đó để mở đúng màn xác nhận, không phải chỉ biết thông báo đã "Processed".
            var draft = await _transactionRepository.GetByNotificationLogIdAsync(command.UserId, processed.Id, ct);
            return new NotificationAnalysisResultDto(processed.Id, processed.Status.ToString(), draft?.Id);
        }

        var account = await _financialAccountRepository.GetByUserAndMonitoredPackageAsync(command.UserId, command.PackageName, ct);
        if (account is null)
        {
            var recentlyIgnored = await _notificationLogRepository.GetRecentIgnoredByContentHashAsync(
                command.UserId, contentHash, now - IgnoredDedupWindow, ct);
            if (recentlyIgnored is not null)
            {
                return new NotificationAnalysisResultDto(recentlyIgnored.Id, "Ignored", null);
            }

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

        var response = await AnalyzeOrMarkFailedAsync(command, log, ct);

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
            // Phase 81: chỉ có nghĩa với tiền vào; lưu để dựng lại gợi ý khi app mở nháp sau này.
            RelationHint = ParseTransactionType(response.Extraction?.TransactionType) == TransactionType.Credit
                ? Transactions.Refunds.IRefundSuggestions.Parse(response.Extraction?.RelationHint)
                : null,
            CreatedAt = now,
        };
        await _notificationLogRepository.AddAiResultAsync(aiResult, ct);

        var outcome = pipelineResult == PipelineResult.Financial
            && aiResult.AmountCents is > 0
            && aiResult.TransactionType is not null
                ? await CreateDraftAsync(command, account, log, aiResult, now, ct)
                : DraftOutcome.None;

        // Phase 82 (BR-DUP-05): cùng một sự kiện ngân hàng gửi lại với nội dung khác (khác contentHash) → bỏ âm thầm, ghi lại vì sao.
        log.Status = outcome.SameEventId is null ? NotificationLogStatus.Processed : NotificationLogStatus.Duplicate;
        log.ProcessedAt = DateTimeOffset.UtcNow;
        await _notificationLogRepository.UpdateAsync(log, ct);

        return new NotificationAnalysisResultDto(
            log.Id,
            log.Status.ToString(),
            outcome.Draft?.Id,
            outcome.Draft is null ? null : aiResult.RelationHint?.ToString(),
            outcome.Suggestions,
            outcome.SameEventId ?? outcome.Draft?.DuplicateOfTransactionId);
    }

    private sealed record DraftOutcome(Transaction? Draft, IReadOnlyList<RefundSuggestionDto>? Suggestions, Guid? SameEventId)
    {
        public static readonly DraftOutcome None = new(null, null, null);
    }

    /// <summary>
    /// Dựng nháp từ kết quả AI, hỏi <see cref="IDuplicateDetector"/> trước khi lưu. Cờ trùng của AI Service chỉ là tín hiệu xếp hạng: nó không còn
    /// làm mất thông báo (Phase 82, BR-DUP-02). Hai lần mua 35k cách nhau 3 phút vì vậy là hai nháp, không phải một.
    /// </summary>
    private async Task<DraftOutcome> CreateDraftAsync(
        AnalyzeNotificationCommand command, FinancialAccount account, NotificationLog log, AiResult aiResult, DateTimeOffset now, CancellationToken ct)
    {
        Category? category = aiResult.CategorySlug is null
            ? null
            : await _categoryRepository.GetSystemBySlugAsync(aiResult.CategorySlug, ct);

        // Chi tiền sau một lần quét QR: lấy người nhận + danh mục người dùng đã chọn thay vì để AI đoán từ nội dung chuyển khoản P2P (thường không có gì để đoán).
        QrTransferIntent? qrIntent = null;
        if (_qrLinker is not null && aiResult.TransactionType == TransactionType.Debit)
        {
            qrIntent = await _qrLinker.FindMatchAsync(command.UserId, command.PackageName, command.ReceivedAt, aiResult.AmountCents!.Value, ct);
            if (qrIntent?.CategoryId is Guid qrCategoryId
                && await _categoryRepository.GetByIdAsync(qrCategoryId, ct) is { IsActive: true } chosen
                && (chosen.UserId is null || chosen.UserId == command.UserId))
            {
                category = chosen;
            }
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            FinancialAccountId = account.Id,
            CategoryId = category?.Id,
            NotificationLogId = log.Id,
            AmountCents = aiResult.AmountCents!.Value,
            TransactionType = aiResult.TransactionType!.Value,
            Source = TransactionSource.Notification,
            Status = TransactionStatus.Draft,
            MerchantName = qrIntent?.ReceiverName ?? aiResult.MerchantName,
            Description = aiResult.Description,
            TransactedAt = aiResult.TransactedAt ?? command.ReceivedAt,
            BalanceAfterCents = aiResult.BalanceAfterCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var duplicates = _duplicates is null
            ? DuplicateMatch.None
            : await _duplicates.MarkSuspectedAsync(transaction, command.PackageName, aiResult.DuplicateRequestId, ct);
        if (duplicates.SameEventId is { } sameEvent)
        {
            return new DraftOutcome(null, null, sameEvent);
        }

        await _transactionRepository.AddAsync(transaction, ct);
        transaction.Category = category;

        if (qrIntent is not null)
        {
            await _qrLinker!.LinkAsync(qrIntent, transaction.Id, ct);
        }

        // Phase 81: AI đoán là hoàn/đảo → tối đa 3 khoản chi gốc gợi ý. Nháp vẫn là nháp; người dùng mới là người liên kết (BR-RF-13).
        IReadOnlyList<RefundSuggestionDto>? suggestions = null;
        if (aiResult.RelationHint is { } hint && _refundSuggestions is not null)
        {
            transaction.FinancialAccount = account;
            suggestions = await _refundSuggestions.SuggestAsync(command.UserId, transaction, hint, ct);
        }

        // Docx bước 5.2/5.3: cùng một giao dịch nháp nhưng hỏi người dùng theo hai cách khác nhau tuỳ mức tin cậy; nghi trùng thì hỏi gộp trước (D15).
        var review = TransactionReviewPushBuilder.Build(
            transaction,
            category?.Name,
            TransactionReviewPushBuilder.ReviewConfidence(aiResult.ExtractionConfidence, aiResult.CategorizationConfidence),
            aiResult.RelationHint,
            suggestions?.Count ?? 0,
            duplicates.Candidates.Count(c => c.MarksSuspected));

        await _pushNotificationService.NotifyAsync(command.UserId, review.Title, review.Body, review.Data, ct);
        return new DraftOutcome(transaction, suggestions, null);
    }

    // Chỉ đọc đồng ý AI ở đây (nhánh thật sự gọi ai-service), không đọc DB thừa cho nhánh Ignored/trùng.
    private async Task<AnalyzeResponse> AnalyzeOrMarkFailedAsync(
        AnalyzeNotificationCommand command, NotificationLog log, CancellationToken ct)
    {
        try
        {
            var allowAi = _consent is not null && await _consent.HasAiConsentAsync(command.UserId, ct);
            return await _aiServiceClient.AnalyzeAsync(
                new AnalyzeRequest(log.Id, command.UserId, command.PackageName, command.NotificationTitle, command.NotificationBody, command.ReceivedAt, allowAi),
                ct);
        }
        catch (AIServiceUnavailableException ex)
        {
            log.Status = NotificationLogStatus.Failed;
            log.ErrorMessage = ex.Message;
            await _notificationLogRepository.UpdateAsync(log, ct);
            throw;
        }
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
