using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class AiResult
{
    public Guid Id { get; set; }
    public Guid NotificationLogId { get; set; }
    public PipelineResult PipelineResult { get; set; }

    public string? ClassifierLabel { get; set; }
    public double? ClassifierConfidence { get; set; }

    public long? AmountCents { get; set; }
    public TransactionType? TransactionType { get; set; }
    public string? MerchantName { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? TransactedAt { get; set; }
    public long? BalanceAfterCents { get; set; }
    public double? ExtractionConfidence { get; set; }

    public string? CategorySlug { get; set; }
    public double? CategorizationConfidence { get; set; }

    public bool IsPotentialDuplicate { get; set; }
    public Guid? DuplicateRequestId { get; set; }

    public string? ClassifierVersion { get; set; }
    public string? ExtractorVersion { get; set; }
    public string? CategorizerVersion { get; set; }

    public int? ProcessingMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public NotificationLog? NotificationLog { get; set; }
}
