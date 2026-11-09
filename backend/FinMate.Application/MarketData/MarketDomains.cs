namespace FinMate.Application.MarketData;

/// <summary>
/// Gộp các tên miền con của cùng một đơn vị, để <c>kinhdoanh.vnexpress.net</c> và <c>vnexpress.net</c> không được tính là hai nguồn độc lập.
/// Đây là ước lượng theo các đuôi phổ biến ở Việt Nam, không dùng danh sách hậu tố đầy đủ. Không chứng minh hai trang thực sự độc lập.
/// </summary>
public static class MarketDomains
{
    private static readonly HashSet<string> SecondLevelSuffixes = new(StringComparer.Ordinal) { "com", "net", "org", "gov", "edu", "ac", "co", "biz", "info", "name", "pro", "health", "int" };

    public static string Of(string url) => OfHost(new Uri(url).Host);

    public static string OfHost(string host)
    {
        var labels = host.ToLowerInvariant().TrimEnd('.').Split('.');
        if (labels.Length <= 2)
        {
            return string.Join('.', labels);
        }

        var take = labels[^1].Length == 2 && SecondLevelSuffixes.Contains(labels[^2]) ? 3 : 2;
        return string.Join('.', labels[^take..]);
    }
}
