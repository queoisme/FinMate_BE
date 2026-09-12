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

// GET /api/v1/stats — route thứ ba của contract, thêm ở Phase 8 cho màn hình quản trị.
// Toàn bộ là số đếm ở mức hệ thống; không có gì gắn với một người dùng cụ thể.
public record AiModelStatus(
    string Stage,
    string? Version,
    DateTimeOffset? TrainedAt,
    double? Accuracy,
    double? MacroF1,
    string? EvaluatedOnSplit);

public record AiTrainingJobStatus(
    string Stage,
    string Status,
    int? SampleCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorMessage);

public record AiServiceStats(
    IReadOnlyList<AiModelStatus> Models,
    int RawSampleCount,
    int LabeledSampleCount,
    int UnlabeledSampleCount,
    IReadOnlyDictionary<string, int> SplitCounts,
    AiTrainingJobStatus? LastTrainingJob,
    int PendingFeedbackCount);

public interface IAIServiceClient
{
    Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default);

    // Best-effort — lỗi không được chặn luồng chính (UpdateTransactionCommand).
    Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default);

    /// <summary>
    /// Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản
    /// hồi. Caller (màn hình quản trị) bắt lấy và vẫn hiển thị phần số liệu của backend —
    /// một dashboard không được sập chỉ vì AI Service đang restart.
    /// </summary>
    Task<AiServiceStats> GetStatsAsync(CancellationToken ct = default);
}
