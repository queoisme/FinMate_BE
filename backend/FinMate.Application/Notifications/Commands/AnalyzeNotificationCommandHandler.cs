using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Budgets;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications;
using FinMate.Application.QrTransfers;
using FinMate.Application.Transactions;
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
    private readonly ISystemConfigProvider _config;
    private readonly ITransactionConfirmationEffects _confirmationEffects;
    private readonly IMerchantCategoryOverrideRepository _merchantCategoryOverrides;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly ICacheService _cache;
    private readonly IAiConsentReader? _consent;  // null: không có bộ đọc đồng ý thì không gửi gì ra bên thứ ba
    private readonly IQrTransferLinker? _qrLinker;  // null: không có luồng quét QR thì giao dịch chỉ dựa vào AI như trước

    public AnalyzeNotificationCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        INotificationLogRepository notificationLogRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAIServiceClient aiServiceClient,
        IPushNotificationService pushNotificationService,
        IValidator<AnalyzeNotificationCommand> validator,
        ISystemConfigProvider config,
        ITransactionConfirmationEffects confirmationEffects,
        IMerchantCategoryOverrideRepository merchantCategoryOverrides,
        IBudgetPeriodService budgetPeriodService,
        ICacheService cache,
        IAiConsentReader? consent = null,
        IQrTransferLinker? qrLinker = null)
    {
        _financialAccountRepository = financialAccountRepository;
        _notificationLogRepository = notificationLogRepository;
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
        _aiServiceClient = aiServiceClient;
        _pushNotificationService = pushNotificationService;
        _validator = validator;
        _config = config;
        _confirmationEffects = confirmationEffects;
        _merchantCategoryOverrides = merchantCategoryOverrides;
        _budgetPeriodService = budgetPeriodService;
        _cache = cache;
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
            var draft = await _transactionRepository.GetByNotificationLogIdAsync(processed.Id, ct);
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
            ProviderReference = response.Extraction?.ProviderReference,
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

        // §5.1: UNCERTAIN cũng phải rơi vào nhánh tạo giao dịch (như một ép buộc về phía Draft) —
        // trước đây chỉ Financial mới qua được, Uncertain không tạo gì cả (sai).
        if ((pipelineResult == PipelineResult.Financial || pipelineResult == PipelineResult.Uncertain)
            && aiResult.AmountCents is > 0
            && aiResult.TransactionType is not null)
        {
            var amount = aiResult.AmountCents.Value;
            var type = aiResult.TransactionType.Value;
            var transactedAt = aiResult.TransactedAt ?? command.ReceivedAt;
            var pipelineSettings = (await _config.GetAsync(ct)).Document.NotificationPipeline;

            // Việc 2 (§5.5): trùng nghiệp vụ THẬT — lọc theo chính ví này, xét cả Debit và Credit.
            // Thay cho aiResult.IsPotentialDuplicate (chỉ cảnh báo, không đáng tin để chặn hẳn).
            //
            // 2026-11-13: kèm providerReference khi có — 2 giao dịch THẬT khác nhau (vd 2 lần
            // quét QR 2.000đ cho 2 người khác nhau trong vòng 5 phút) cùng ví+số tiền+chiều tiền
            // trước đây bị hiểu lầm là 1 giao dịch gửi lặp vì không có gì phân biệt được nội dung.
            var isBusinessDuplicate = await _transactionRepository.HasAuthoritativeDuplicateAsync(
                account.Id, amount, type, transactedAt,
                TimeSpan.FromMinutes(pipelineSettings.BusinessDuplicateWindowMinutes), null, ct,
                aiResult.ProviderReference);

            if (!isBusinessDuplicate)
            {
                // Việc 1 (§7/§16.1): tính Auto-commit TRƯỚC khi quyết định tạo gì — chỉ khi nhãn
                // Financial (không bao giờ cho Uncertain, dù confidence có cao thế nào).
                //
                // Ngưỡng Auto-commit chỉ xét ExtractionConfidence — KHÔNG trộn với
                // CategorizationConfidence (khác với ReviewConfidence dùng cho nội dung push ở
                // nhánh Draft bên dưới, giữ nguyên không đổi). §7 nói rõ category confidence thấp
                // không được chặn Auto-commit — bug thật phát hiện qua test thiết bị: 1 giao dịch
                // 2.000đ đọc số sạch vẫn rơi về Draft chỉ vì người nhận ("Tuân") không quen nên AI
                // phân loại thiếu tự tin.
                var confidence = TransactionReviewPushBuilder.ReviewConfidence(
                    aiResult.ExtractionConfidence, aiResult.CategorizationConfidence);
                var autoCommitEligible = pipelineResult == PipelineResult.Financial
                    && aiResult.ExtractionConfidence is { } extractionConfidence
                    && extractionConfidence >= TransactionReviewPushBuilder.OneTapThreshold;

                // Việc 4 (§16.3): có vế đối ứng của 1 lần chuyển khoản nội bộ đang chờ Draft không?
                var mergeCandidate = await _transactionRepository.FindMergeCandidateAsync(
                    command.UserId, amount, Opposite(type), account.Id, transactedAt,
                    TimeSpan.FromMinutes(pipelineSettings.SelfTransferMergeWindowMinutes), ct);

                if (mergeCandidate is not null)
                {
                    draftTransactionId = await MergeIntoTransferAsync(mergeCandidate, account, type, amount, ct);

                    // Bug thật, 2026-11-13: leg này có thể cũng xuất phát từ 1 lần quét QR (tự
                    // chuyển cho chính mình giữa 2 ví) — nhánh merge trước đây bỏ qua hoàn toàn
                    // việc khớp QR, nên QrTransferIntent/PendingQrTransfer không bao giờ được đóng
                    // lại dù D1 đã ghi đúng là Transfer: C3 (Lịch sử quét QR) kẹt "Chờ ngân hàng
                    // xác nhận" mãi mãi.
                    if (_qrLinker is not null && type == TransactionType.Debit)
                    {
                        var mergedQrIntent = await _qrLinker.FindMatchAsync(command.UserId, command.PackageName, command.ReceivedAt, amount, ct);
                        if (mergedQrIntent is not null)
                        {
                            await _qrLinker.LinkAsync(mergedQrIntent, mergeCandidate.Id, ct);
                        }
                    }

                    // Transfer đã Confirmed ngay (§16.3) — nếu vế đầu từng nhận push "Chờ duyệt",
                    // phải sửa lại cảm nhận bằng 1 push mới, không để im lặng biến mất.
                    await _pushNotificationService.NotifyAsync(
                        mergeCandidate.UserId,
                        "Đã tự động ghi nhận",
                        "Đã nhận diện là chuyển khoản nội bộ giữa 2 ví của bạn, đã tự động ghi nhận.",
                        new Dictionary<string, string>
                        {
                            ["type"] = "transfer_merged",
                            ["transactionId"] = mergeCandidate.Id.ToString(),
                        },
                        ct);
                }
                else
                {
                    Category? category = aiResult.CategorySlug is null
                        ? null
                        : await _categoryRepository.GetSystemBySlugAsync(aiResult.CategorySlug, ct);

                    // Issue B: tên hiển thị người dùng đã lưu/đổi tay cho người nhận này (nếu có)
                    // đáng tin hơn tên thô trong thông báo/mã QR — chuỗi dự phòng cuối cùng là
                    // savedDisplayName -> qrIntent?.ReceiverName -> aiResult.MerchantName.
                    string? savedDisplayName = null;

                    // Việc 5 (§7 priority #1, §16.5): override theo tên merchant, cho case KHÔNG
                    // qua QR — chỉ áp khi chưa có gì khác (AI slug) và trước khi QR override (nếu
                    // có) ghi đè tiếp ở dưới, vì QR có dữ liệu đáng tin hơn.
                    if (!string.IsNullOrWhiteSpace(aiResult.MerchantName))
                    {
                        var merchantOverride = await _merchantCategoryOverrides.GetAsync(
                            command.UserId, MerchantCategoryKey.Normalize(aiResult.MerchantName), ct);
                        if (merchantOverride is not null)
                        {
                            category = merchantOverride.Category;
                            savedDisplayName = merchantOverride.DisplayName;
                        }
                    }

                    // Chi tiền sau một lần quét QR: lấy người nhận + danh mục người dùng đã chọn thay vì để AI đoán từ nội dung chuyển khoản P2P (thường không có gì để đoán).
                    QrTransferIntent? qrIntent = null;
                    if (_qrLinker is not null && type == TransactionType.Debit)
                    {
                        qrIntent = await _qrLinker.FindMatchAsync(command.UserId, command.PackageName, command.ReceivedAt, amount, ct);
                        if (qrIntent is not null)
                        {
                            // Người nhận có dữ liệu cấu trúc (BankBin + AccountNumber) qua QR —
                            // đáng tin hơn chuỗi merchant tự do của case không-QR.
                            savedDisplayName = await _qrLinker.GetSavedDisplayNameAsync(
                                command.UserId, qrIntent.ReceiverBankBin, qrIntent.ReceiverAccountNumber, ct) ?? savedDisplayName;
                        }

                        if (qrIntent?.CategoryId is Guid qrCategoryId
                            && await _categoryRepository.GetByIdAsync(qrCategoryId, ct) is { IsActive: true } chosen
                            && (chosen.UserId is null || chosen.UserId == command.UserId))
                        {
                            category = chosen;
                        }
                    }

                    var transaction = new Transaction
                    {
                        UserId = command.UserId,
                        FinancialAccountId = account.Id,
                        CategoryId = category?.Id,
                        NotificationLogId = log.Id,
                        AmountCents = amount,
                        TransactionType = type,
                        Source = TransactionSource.Notification,
                        Status = autoCommitEligible ? TransactionStatus.Confirmed : TransactionStatus.Draft,
                        MerchantName = savedDisplayName ?? qrIntent?.ReceiverName ?? aiResult.MerchantName,
                        Description = aiResult.Description,
                        TransactedAt = transactedAt,
                        BalanceAfterCents = aiResult.BalanceAfterCents,  // giữ nguyên số AI đọc từ bank, không ghi đè bằng account.BalanceCents
                        ProviderReference = aiResult.ProviderReference,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };

                    IReadOnlyList<BudgetAlert> budgetAlerts = [];
                    if (autoCommitEligible)
                    {
                        budgetAlerts = await _confirmationEffects.ApplyAsync(transaction, account, ct);
                    }

                    await _transactionRepository.AddAsync(transaction, ct);
                    draftTransactionId = transaction.Id;
                    transaction.Category = category;

                    if (qrIntent is not null)
                    {
                        await _qrLinker!.LinkAsync(qrIntent, transaction.Id, ct);
                    }

                    if (autoCommitEligible)
                    {
                        await _confirmationEffects.NotifyAsync(transaction, budgetAlerts, ct);

                        // Việc 1: KHÔNG im lặng — tiền đã bị trừ/cộng tự động, phải báo rõ khác
                        // với push "Chờ duyệt" (đây KHÔNG còn gì để duyệt nữa).
                        var autoPush = TransactionReviewPushBuilder.BuildAutoCommittedPush(transaction, category?.Name);
                        await _pushNotificationService.NotifyAsync(
                            command.UserId, autoPush.Title, autoPush.Body, autoPush.Data, ct);
                    }
                    else
                    {
                        // Docx bước 5.2/5.3: cùng một giao dịch nháp nhưng hỏi người dùng theo hai
                        // cách khác nhau tuỳ mức tin cậy.
                        var review = TransactionReviewPushBuilder.Build(transaction, category?.Name, confidence);
                        await _pushNotificationService.NotifyAsync(
                            command.UserId, review.Title, review.Body, review.Data, ct);
                    }
                }
            }
            // isBusinessDuplicate == true: không tạo gì cả — log.Status đã Processed từ trước.
            // Không có cột nào trên NotificationLog để đánh dấu "trùng với giao dịch X"; chấp nhận
            // giới hạn này thay vì tự ý thêm cột/migration ngoài phạm vi đã duyệt (§13).
        }

        return new NotificationAnalysisResultDto(log.Id, log.Status.ToString(), draftTransactionId);
    }

    private static TransactionType Opposite(TransactionType type) => type switch
    {
        TransactionType.Debit => TransactionType.Credit,
        TransactionType.Credit => TransactionType.Debit,
        _ => type,
    };

    /// <summary>
    /// Gộp leg hiện tại vào vế đối ứng (<paramref name="mergeCandidate"/>) thành 1 giao dịch
    /// Transfer đã Confirmed (§16.3) — không tạo transaction mới cho leg hiện tại.
    ///
    /// Issue A (2026-11-13, user chọn Option A): <paramref name="mergeCandidate"/> có thể đã
    /// Confirmed từ trước (Auto-commit chạy gần như ngay lập tức từ §16.6 khiến việc này phổ biến
    /// hơn hẳn so với lúc §16.3 được quyết định, khi giả định ngầm là 2 thông báo luôn tới trước
    /// khi người dùng — hay Auto-commit — có cơ hội xác nhận vế đầu). Nếu vậy, phải ĐẢO NGƯỢC
    /// hiệu ứng số dư/ngân sách đã áp dụng cho vế đó trước khi gộp, cùng công thức đảo dấu đã dùng ở
    /// <c>DeleteTransactionCommandHandler</c> — không thì tiền/ngân sách bị áp 2 lần (1 lần lúc
    /// Auto-commit vế đầu, 1 lần ở merge dưới đây cho giao dịch Transfer gộp).
    /// </summary>
    private async Task<Guid> MergeIntoTransferAsync(
        Transaction mergeCandidate, FinancialAccount incomingAccount, TransactionType incomingType,
        long amount, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var sourceAccountId = incomingType == TransactionType.Debit ? incomingAccount.Id : mergeCandidate.FinancialAccountId;
        var destAccountId = incomingType == TransactionType.Debit ? mergeCandidate.FinancialAccountId : incomingAccount.Id;

        var sourceAccount = sourceAccountId == incomingAccount.Id
            ? incomingAccount
            : await _financialAccountRepository.GetByIdAsync(sourceAccountId, mergeCandidate.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", sourceAccountId);
        var destAccount = destAccountId == incomingAccount.Id
            ? incomingAccount
            : await _financialAccountRepository.GetByIdAsync(destAccountId, mergeCandidate.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", destAccountId);

        if (mergeCandidate.Status == TransactionStatus.Confirmed)
        {
            // mergeCandidate.FinancialAccountId vẫn còn là ví GỐC của vế đầu lúc này (chưa bị
            // gán lại thành sourceAccountId ở dưới) — đúng chính ví mà Auto-commit đã áp hiệu ứng.
            var mergeCandidateAccount = mergeCandidate.FinancialAccountId == sourceAccount.Id
                ? sourceAccount
                : mergeCandidate.FinancialAccountId == destAccount.Id
                    ? destAccount
                    : await _financialAccountRepository.GetByIdAsync(mergeCandidate.FinancialAccountId, mergeCandidate.UserId, ct)
                        ?? throw new NotFoundException("FinancialAccount", mergeCandidate.FinancialAccountId);

            mergeCandidateAccount.BalanceCents -= mergeCandidate.TransactionType == TransactionType.Credit
                ? mergeCandidate.AmountCents
                : -mergeCandidate.AmountCents;
            mergeCandidateAccount.UpdatedAt = now;

            // Đảo dấu delta ngân sách đã áp — luôn 0 với Credit/Transfer (Spend chỉ tính Debit),
            // và không bao giờ chạm ngưỡng cảnh báo giống nhánh Delete, bỏ qua BudgetAlert trả về.
            await _budgetPeriodService.ApplyDeltaAsync(
                mergeCandidate.UserId,
                mergeCandidate.CategoryId,
                -TransactionBudgetDelta.Spend(mergeCandidate.TransactionType, mergeCandidate.AmountCents, mergeCandidate.Category),
                mergeCandidate.TransactedAt,
                ct);
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, mergeCandidate.UserId, mergeCandidate.TransactedAt, ct);
        }

        sourceAccount.BalanceCents -= amount;
        sourceAccount.UpdatedAt = now;
        destAccount.BalanceCents += amount;
        destAccount.UpdatedAt = now;

        mergeCandidate.FinancialAccountId = sourceAccountId;
        mergeCandidate.CounterAccountId = destAccountId;
        mergeCandidate.TransactionType = TransactionType.Transfer;
        mergeCandidate.CategoryId = null;
        mergeCandidate.Status = TransactionStatus.Confirmed;
        mergeCandidate.BalanceAfterCents = sourceAccount.BalanceCents;
        mergeCandidate.UpdatedAt = now;
        await _transactionRepository.UpdateAsync(mergeCandidate, ct);
        return mergeCandidate.Id;
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
