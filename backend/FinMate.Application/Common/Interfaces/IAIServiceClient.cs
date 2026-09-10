namespace FinMate.Application.Common.Interfaces;

// Record types khớp field-cho-field với contract Backend <-> AI Service ở
// ARCHITECTURE.md §3.3. UserId ở đây là Guid thật — việc băm SHA-256 thành
// user_id_hash trước khi gửi qua HTTP là trách nhiệm của Infrastructure
// (AGENTS.md §3.1: AI DB không lưu user_id thực), Application không cần biết.
public record AnalyzeRequest(
    Guid BackendRequestId,
    Guid UserId,
    string PackageName,
    string? NotificationTitle,
    string NotificationBody,
    DateTimeOffset ReceivedAt);

public record ClassifierResult(string Label, double Confidence);

public record ExtractionResult(
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    string? Description,
    DateTimeOffset? TransactedAt,
    long? BalanceAfterCents,
    double? Confidence);

public record CategorizationResult(string? CategorySlug, double? Confidence);

public record DuplicateResult(bool IsPotentialDuplicate, Guid? DuplicateRequestId);

public record ModelVersions(string? Classifier, string? Extractor, string? Categorizer);

public record AnalyzeResponse(
    string PipelineResult,
    ClassifierResult Classifier,
    ExtractionResult? Extraction,
    CategorizationResult? Categorization,
    DuplicateResult? Duplicate,
    ModelVersions? ModelVersions,
    int? ProcessingMs);

// TransactionId/UserId ở đây là Guid thật — băm SHA-256 trước khi gửi qua HTTP cũng là
// trách nhiệm của Infrastructure, giống AnalyzeRequest.UserId ở trên.
public record FeedbackRequest(
    Guid TransactionId,
    Guid UserId,
    Guid? PipelineRequestId,
    string PackageName,
    string? PredictedCategory,
    string? CorrectedCategory,
    string FeedbackType);

public interface IAIServiceClient
{
    Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default);

    // Best-effort — lỗi không được chặn luồng chính (UpdateTransactionCommand).
    Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default);
}
