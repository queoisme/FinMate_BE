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

    /// <summary>
    /// Mã tham chiếu ngân hàng của chính giao dịch này khi provider bắt được, có thể đã bị ẩn
    /// một phần (vd "xxxxxxxx7666") — null khi provider không có (2026-11-13).
    /// </summary>
    public string? ProviderReference { get; set; }

    public string? CategorySlug { get; set; }
    public double? CategorizationConfidence { get; set; }

    public bool IsPotentialDuplicate { get; set; }
    public Guid? DuplicateRequestId { get; set; }

    /// <summary>
    /// Gợi ý của AI: hoàn tiền, đảo giao dịch (chỉ tiền vào, Phase 81) hay chuyển giữa các ví của chính người dùng (cả hai chiều, Phase 83).
    /// Chỉ để gợi ý, không bao giờ tự liên kết hay tự chuyển đổi.
    /// </summary>
    public NotificationRelationHint? RelationHint { get; set; }

    /// <summary>Loại chuyển ví khi <see cref="RelationHint"/> là <c>Transfer</c>.</summary>
    public TransferKind? TransferKind { get; set; }

    /// <summary>Độ mạnh của gợi ý khi <see cref="RelationHint"/> là <c>DebtRepayment</c> (Phase 90).</summary>
    public DebtHintStrength? DebtHintStrength { get; set; }

    public string? ClassifierVersion { get; set; }
    public string? ExtractorVersion { get; set; }
    public string? CategorizerVersion { get; set; }

    public int? ProcessingMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public NotificationLog? NotificationLog { get; set; }
}
