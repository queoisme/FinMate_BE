using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Một website admin đưa vào. Tắt bằng <see cref="IsActive"/>, không xóa, để bài và giá đã đọc vẫn truy được nguồn.</summary>
public class MarketSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public MarketSourceKind Kind { get; set; }

    /// <summary>False khi <c>robots.txt</c> của trang chặn bot AI: vẫn đọc được bảng giá, nhưng không gửi nội dung cho AI.</summary>
    public bool AiAllowed { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastContentHash { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Một bài liên quan đã đọc. Cố ý không có toàn văn: chỉ giữ đủ để không đọc lại và truy được về bài gốc.</summary>
public class MarketDocument
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ContentHash { get; set; }
    public int TextLength { get; set; }
    public MarketDocumentStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>Giá đã xác nhận. Chỉ thêm dòng khi giá đổi, nên dòng mới nhất của một loại là giá đang áp dụng.</summary>
public class FuelPrice
{
    public Guid Id { get; set; }
    public string FuelKey { get; set; } = string.Empty;
    public long Region1PriceCents { get; set; }
    public long Region2PriceCents { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public FuelPriceOrigin Origin { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? Note { get; set; }
}
