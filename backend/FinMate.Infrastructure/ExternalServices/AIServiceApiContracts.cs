using System.Text.Json.Serialization;

namespace FinMate.Infrastructure.ExternalServices;

// DTO thô khớp đúng JSON contract ở ARCHITECTURE.md §3.3 (snake_case) — chỉ dùng nội bộ
// Infrastructure, không lộ ra Application (Application dùng IAIServiceClient + record PascalCase).
public record AnalyzeApiRequest(
    [property: JsonPropertyName("backend_request_id")] Guid BackendRequestId,
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("notification_title")] string? NotificationTitle,
    [property: JsonPropertyName("notification_body")] string NotificationBody,
    [property: JsonPropertyName("received_at")] DateTimeOffset ReceivedAt);

public record ClassifierApiResult(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("confidence")] double Confidence);

public record ExtractionApiResult(
    [property: JsonPropertyName("amount_cents")] long? AmountCents,
    [property: JsonPropertyName("transaction_type")] string? TransactionType,
    [property: JsonPropertyName("merchant_name")] string? MerchantName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("transacted_at")] DateTimeOffset? TransactedAt,
    [property: JsonPropertyName("balance_after_cents")] long? BalanceAfterCents,
    [property: JsonPropertyName("confidence")] double? Confidence);

public record CategorizationApiResult(
    [property: JsonPropertyName("category_slug")] string? CategorySlug,
    [property: JsonPropertyName("confidence")] double? Confidence);

public record DuplicateApiResult(
    [property: JsonPropertyName("is_potential_duplicate")] bool IsPotentialDuplicate,
    [property: JsonPropertyName("duplicate_request_id")] Guid? DuplicateRequestId);

public record ModelVersionsApiResult(
    [property: JsonPropertyName("classifier")] string? Classifier,
    [property: JsonPropertyName("extractor")] string? Extractor,
    [property: JsonPropertyName("categorizer")] string? Categorizer);

public record AnalyzeApiResponse(
    [property: JsonPropertyName("pipeline_result")] string PipelineResult,
    [property: JsonPropertyName("classifier")] ClassifierApiResult Classifier,
    [property: JsonPropertyName("extraction")] ExtractionApiResult? Extraction,
    [property: JsonPropertyName("categorization")] CategorizationApiResult? Categorization,
    [property: JsonPropertyName("duplicate")] DuplicateApiResult? Duplicate,
    [property: JsonPropertyName("model_versions")] ModelVersionsApiResult? ModelVersions,
    [property: JsonPropertyName("processing_ms")] int? ProcessingMs);

public record FeedbackApiRequest(
    [property: JsonPropertyName("backend_transaction_id_hash")] string BackendTransactionIdHash,
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("pipeline_request_id")] Guid? PipelineRequestId,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("predicted_category")] string? PredictedCategory,
    [property: JsonPropertyName("corrected_category")] string? CorrectedCategory,
    [property: JsonPropertyName("feedback_type")] string FeedbackType);
