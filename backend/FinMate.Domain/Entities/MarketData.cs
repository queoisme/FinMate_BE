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

    /// <summary>Lần gần nhất gửi bài này cho AI, kể cả khi lỗi. Dùng để đếm trần số bài mỗi ngày.</summary>
    public DateTimeOffset? AiAttemptedAt { get; set; }
}

/// <summary>Thông tin AI rút ra từ một bài, đã đối chiếu đoạn trích với nguyên văn bài.</summary>
public class MarketSignal
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public MarketTopic Topic { get; set; }
    public MarketDirection Direction { get; set; }
    public MarketFactStatus FactStatus { get; set; }
    public decimal? ChangePercent { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Nhiều trang cùng đưa tin một thay đổi giá. Chỉ sự kiện đã duyệt mới được xét gửi cảnh báo.</summary>
public class MarketEvent
{
    public Guid Id { get; set; }
    public MarketTopic Topic { get; set; }
    public MarketDirection Direction { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int SourceCount { get; set; }
    public MarketEventStatus Status { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    /// <summary>Đã xét gửi cảnh báo. Có mốc này nên job chạy lại không xét lần hai, còn job chết giữa chừng thì lượt sau làm tiếp.</summary>
    public DateTimeOffset? AlertsProcessedAt { get; set; }
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

    /// <summary>Đã xét gửi cảnh báo cho mức giá này.</summary>
    public DateTimeOffset? AlertsProcessedAt { get; set; }
}
