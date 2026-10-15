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

public record ModelStatusApiResult(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("trained_at")] DateTimeOffset? TrainedAt,
    [property: JsonPropertyName("accuracy")] double? Accuracy,
    [property: JsonPropertyName("macro_f1")] double? MacroF1,
    [property: JsonPropertyName("evaluated_on_split")] string? EvaluatedOnSplit);

public record TrainingJobApiResult(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sample_count")] int? SampleCount,
    [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("finished_at")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("error_message")] string? ErrorMessage);

public record StatsApiResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<ModelStatusApiResult> Models,
    [property: JsonPropertyName("raw_sample_count")] int RawSampleCount,
    [property: JsonPropertyName("labeled_sample_count")] int LabeledSampleCount,
    [property: JsonPropertyName("unlabeled_sample_count")] int UnlabeledSampleCount,
    [property: JsonPropertyName("split_counts")] IReadOnlyDictionary<string, int> SplitCounts,
    [property: JsonPropertyName("last_training_job")] TrainingJobApiResult? LastTrainingJob,
    [property: JsonPropertyName("pending_feedback_count")] int PendingFeedbackCount);

public record OcrApiResponse(
    [property: JsonPropertyName("ocr_result")] string OcrResult,
    [property: JsonPropertyName("extraction")] ExtractionApiResult? Extraction,
    [property: JsonPropertyName("categorization")] CategorizationApiResult? Categorization,
    [property: JsonPropertyName("processing_ms")] int? ProcessingMs);

public record ChatParseApiRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("user_id_hash")] string UserIdHash);

public record ChatEntryApiResult(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("amount_cents")] long AmountCents,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("merchant")] string? Merchant,
    [property: JsonPropertyName("category_slug")] string? CategorySlug,
    [property: JsonPropertyName("income_type")] string? IncomeType,
    [property: JsonPropertyName("auto_save_ok")] bool AutoSaveOk,
    [property: JsonPropertyName("to_others")] bool ToOthers,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset? OccurredAt = null,
    [property: JsonPropertyName("category_source")] string? CategorySource = null);

public record ChatQueryApiResult(
    [property: JsonPropertyName("metric")] string Metric,
    [property: JsonPropertyName("period_label")] string PeriodLabel,
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("end")] DateTimeOffset End,
    [property: JsonPropertyName("category_slug")] string? CategorySlug,
    [property: JsonPropertyName("income_type")] string? IncomeType);

public record ChatParseApiResponse(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("reply_text")] string? ReplyText,
    [property: JsonPropertyName("entry")] ChatEntryApiResult? Entry,
    [property: JsonPropertyName("query")] ChatQueryApiResult? Query,
    [property: JsonPropertyName("processing_ms")] int? ProcessingMs,
    [property: JsonPropertyName("entries")] IReadOnlyList<ChatEntryApiResult>? Entries = null,
    [property: JsonPropertyName("total_amount")] long? TotalAmount = null,
    [property: JsonPropertyName("total_mismatch")] bool TotalMismatch = false,
    [property: JsonPropertyName("skipped")] IReadOnlyList<string>? Skipped = null);

public record BillClassifyApiRequest(
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("merchant_name")] string? MerchantName,
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines);

public record BillClassifyApiResponse(
    [property: JsonPropertyName("category_slug")] string? CategorySlug,
    [property: JsonPropertyName("local_category")] string? LocalCategory,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("source")] string Source);

public record BillLearnApiRequest(
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("merchant_name")] string MerchantName,
    [property: JsonPropertyName("category_slug")] string CategorySlug);

public record BillSampleApiRequest(
    [property: JsonPropertyName("scan_id")] Guid ScanId,
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("merchant_name")] string? MerchantName,
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines,
    [property: JsonPropertyName("total_amount")] long? TotalAmount,
    [property: JsonPropertyName("predicted_slug")] string? PredictedSlug,
    [property: JsonPropertyName("predicted_source")] string? PredictedSource);

public record BillSampleLabelApiRequest(
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("final_slug")] string FinalSlug);

public record BillSampleDiscardApiRequest(
    [property: JsonPropertyName("user_id_hash")] string UserIdHash);

public record BillSampleEraseApiRequest(
    [property: JsonPropertyName("user_id_hash")] string UserIdHash);

public record ClassifyItemApiInput(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent")] int? Parent);

public record ClassifyItemsApiRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<ClassifyItemApiInput> Items,
    [property: JsonPropertyName("merchant_name")] string? MerchantName,
    [property: JsonPropertyName("user_id_hash")] string UserIdHash,
    [property: JsonPropertyName("allow_ai")] bool AllowAi);

public record ClassifiedItemApiResult(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("category_slug")] string? CategorySlug,
    [property: JsonPropertyName("source")] string Source);

public record ClassifyItemsApiResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ClassifiedItemApiResult> Items,
    [property: JsonPropertyName("ai_used")] bool AiUsed);
