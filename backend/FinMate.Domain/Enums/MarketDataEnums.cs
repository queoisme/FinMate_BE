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
