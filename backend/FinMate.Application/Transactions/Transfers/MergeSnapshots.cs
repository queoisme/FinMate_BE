using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>Ghi ảnh chụp trong cùng DB transaction với lượt gộp / chuyển đổi (Phase 83, phần B). Chỉ thêm vào ngữ cảnh, không lưu.</summary>
public interface IMergeSnapshotRepository
{
    void Add(TransactionMergeSnapshot snapshot);

    /// <summary>Xoá ảnh chụp cũ hơn <paramref name="cutoff"/> (S5). Trả số dòng đã xoá.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}

/// <summary>Trạng thái một dòng giao dịch TRƯỚC thao tác: đủ để dựng lại khi hoàn tác.</summary>
public sealed record SnapshotRow(
    Guid Id,
    string Type,
    string Status,
    long AmountCents,
    Guid AccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    DateTimeOffset TransactedAt,
    long? BalanceAfterCents,
    string? Description,
    string? MerchantName,
    Guid? NotificationLogId,
    Guid? DuplicateOfTransactionId,
    string? DuplicateStatus,
    string[]? MergedSources,
    bool ExcludeFromForecast,
    string? ForecastExclusionReason,
    DateTimeOffset? TransferDismissedAt)
{
    public static SnapshotRow Of(Transaction t) => new(
        t.Id,
        t.TransactionType.ToString(),
        t.Status.ToString(),
        t.AmountCents,
        t.FinancialAccountId,
        t.CounterAccountId,
        t.CategoryId,
        t.TransactedAt,
        t.BalanceAfterCents,
        t.Description,
        t.MerchantName,
        t.NotificationLogId,
        t.DuplicateOfTransactionId,
        t.DuplicateStatus?.ToString(),
        t.MergedSources,
        t.ExcludeFromForecast,
        t.ForecastExclusionReason?.ToString(),
        t.TransferDismissedAt);
}

/// <summary>Toàn bộ nội dung cột <c>before</c>. Danh sách tham chiếu là id đã chuyển hoặc đã gỡ, để hoàn tác biết phải trả gì về đâu.</summary>
public sealed record SnapshotBefore(
    IReadOnlyList<SnapshotRow> Rows,
    IReadOnlyDictionary<Guid, long> BalanceDeltas,
    Guid? FeeTransactionId = null,
    IReadOnlyList<Guid>? MovedQrIntents = null,
    IReadOnlyList<Guid>? ReleasedDraftRefunds = null,
    IReadOnlyList<Guid>? RepointedDuplicateLinks = null,
    IReadOnlyList<Guid>? ClosedAnomalies = null,
    bool ReceiptLinkMoved = false);

public static class MergeSnapshotFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TransactionMergeSnapshot Create(
        Guid userId, Guid survivorId, Guid? mergedId, MergeSnapshotKind kind, TransferKind? transferKind, SnapshotBefore before, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SurvivorId = survivorId,
            MergedId = mergedId,
            Kind = kind,
            TransferKind = transferKind,
            Before = JsonSerializer.Serialize(before, Json),
            CreatedAt = now,
        };
}
