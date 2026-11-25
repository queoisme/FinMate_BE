using FinMate.Domain.Enums;

namespace FinMate.Application.DataExport;

/// <summary>
/// Một giao dịch đã đọc sẵn tên (ví, danh mục, nhóm) để ghi CSV (Phase 91, BR-EXP-04). Cố ý không có số dư sau giao dịch, <c>clientRequestId</c>,
/// id log thông báo, mã tham chiếu ngân hàng hay dữ liệu AI (BR-EXP-05): thêm trường vào đây là thêm vào file người dùng tải về.
/// </summary>
public sealed record TransactionExportRow
{
    public required Guid Id { get; init; }
    public required DateTimeOffset TransactedAt { get; init; }
    public required TransactionType Type { get; init; }
    public required long AmountCents { get; init; }
    public required string AccountName { get; init; }
    public string? CounterAccountName { get; init; }
    public string? CategoryName { get; init; }
    public string? CategorySlug { get; init; }
    public string? MerchantName { get; init; }
    public string? Description { get; init; }
    public required TransactionSource Source { get; init; }
    public required TransactionStatus Status { get; init; }
    public TransactionRelation? Relation { get; init; }
    public bool IsReversed { get; init; }
    public Guid? RelatedTransactionId { get; init; }

    /// <summary>Lệnh đảo trỏ vào giao dịch này (khi <see cref="IsReversed"/>).</summary>
    public Guid? ReversedByTransactionId { get; init; }

    /// <summary>Khoản vay có dòng gắn giao dịch này (gốc/trả hoặc phần lãi tách riêng).</summary>
    public Guid? LoanId { get; init; }

    /// <summary>Giao dịch là phần lãi tách ra khi trả vượt gốc (Phase 90, S1).</summary>
    public bool IsLoanInterest { get; init; }

    public Guid? GroupFundEntryId { get; init; }
    public DuplicateStatus? DuplicateStatus { get; init; }
    public Guid? DuplicateOfTransactionId { get; init; }
    public IReadOnlyList<string>? MergedSources { get; init; }

    /// <summary>Tên nhóm khi CHÍNH user đã chia sẻ giao dịch này vào nhóm.</summary>
    public string? GroupName { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
    public Guid? MergedIntoTransactionId { get; init; }
}
