namespace FinMate.Domain.Enums;

public enum MarketSourceKind
{
    /// <summary>RSS hoặc Atom.</summary>
    Feed,

    /// <summary>Trang có bảng giá xăng, đọc bằng luật.</summary>
    PriceTable,

    /// <summary>Trang danh sách bài, lấy link trong cùng tên miền.</summary>
    Page,
}

public enum MarketDocumentStatus
{
    /// <summary>Đã lấy được nội dung, chờ AI đọc.</summary>
    PendingAi,

    /// <summary>AI đã đọc xong, có thể không rút ra được gì.</summary>
    Processed,

    /// <summary>Nguồn không cho phép dùng nội dung với AI.</summary>
    AiNotAllowed,

    /// <summary>Trang không có đủ nội dung trong HTML, thường vì cần JavaScript.</summary>
    NoContent,

    /// <summary>Bị <c>robots.txt</c> hoặc chính trang đó từ chối.</summary>
    Blocked,
    Failed,
}

public enum FuelPriceOrigin
{
    Sources,
    Manual,
}

public enum MarketDirection
{
    Up,
    Down,
    Stable,
}

public enum MarketFactStatus
{
    /// <summary>Giá đã đổi, hoặc đã có quyết định chính thức.</summary>
    Actual,

    /// <summary>Mới là dự báo, đề xuất, ý kiến. Được lưu nhưng không bao giờ thành sự kiện.</summary>
    Forecast,
}

public enum MarketEventStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
}
