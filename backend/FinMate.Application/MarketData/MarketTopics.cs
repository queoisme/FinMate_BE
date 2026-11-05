namespace FinMate.Application.MarketData;

/// <summary>Lọc bài trước khi tốn lượt đọc: một RSS có 60 tin nhưng chỉ vài tin nói về giá cả ảnh hưởng tới chi tiêu.</summary>
public static class MarketTopics
{
    private static readonly string[] Keywords =
    [
        "giá xăng", "xăng dầu", "ron 95", "ron95", "ron 92", "e5 ron", "e10",
        "giá điện", "tiền điện", "biểu giá điện",
        "giá nước", "tiền nước",
        "giá thực phẩm", "giá thịt", "giá heo", "giá lợn", "giá rau", "giá gạo", "giá trứng",
        "giá tiêu dùng", "cpi", "lạm phát",
    ];

    public static bool IsRelevant(string? title, string? summary = null)
    {
        var text = $"{title} {summary}".ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC);
        return Keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }
}
