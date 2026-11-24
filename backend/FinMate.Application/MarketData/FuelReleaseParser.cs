using System.Globalization;
using System.Text.RegularExpressions;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

/// <summary>
/// Đọc thông cáo điều hành giá xăng dầu của Bộ Công Thương. Trang chỉ mục → link đầu tiên có chữ <see cref="MarketDataSettings.FuelReleaseLinkText"/>
/// → câu "{tên hàng}: không cao hơn {số} đồng/lít". Đó là giá trần toàn quốc nên không có giá vùng 2.
/// Chỉ nhận câu đúng mẫu đó: câu "giảm 823 đồng/lít so với giá bán tối đa hiện hành" đứng sau số nên không bao giờ được đọc nhầm thành giá.
/// </summary>
public static class FuelReleaseParser
{
    public static Uri? FindLatestRelease(string indexHtml, Uri indexUri, MarketDataSettings settings)
    {
        var site = MarketDomains.OfHost(indexUri.Host);
        foreach (var link in HtmlTextExtractor.Links(indexHtml, indexUri))
        {
            if (link.Text.Contains(settings.FuelReleaseLinkText, StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(link.Url, UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps
                && MarketDomains.OfHost(url.Host) == site)
            {
                return url;
            }
        }

        return null;
    }

    public static IReadOnlyList<FuelReading> Parse(string pageText, MarketDataSettings settings)
    {
        var readings = new List<FuelReading>();
        foreach (var name in settings.FuelReleaseNames)
        {
            var label = string.Join(@"\s+", name.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            var match = Regex.Match(
                pageText,
                label + @"\s*:\s*không\s+cao\s+hơn\s+(?<price>\d{1,3}(?:[.,]\d{3})+)\s*đồng\s*/\s*lít",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (match.Success
                && long.TryParse(match.Groups["price"].Value.Replace(".", string.Empty).Replace(",", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var price))
            {
                readings.Add(new FuelReading(name.FuelKey, price, null));
            }
        }

        return readings;
    }
}
