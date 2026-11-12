using System.Net;
using System.Text.RegularExpressions;

namespace FinMate.Application.Economics;

/// <summary>Số liệu một thông cáo chỉ số giá tiêu dùng (điểm cơ bản: 457 = 4,57%; âm khi giảm).</summary>
public sealed record CpiRelease(int Year, int Month, int AveragePercentBps, int YearOnYearBps, int MonthOnMonthBps, DateOnly PublishedOn);

/// <summary>
/// Đọc thông cáo CPI của Cục Thống kê (nso.gov.vn) từ HTML. Hàm thuần: chỉ đọc chữ hiển thị, không gọi mạng. Trả <c>null</c> khi trang không phải thông cáo CPI
/// hoặc thiếu một trong các con số cần (bình quân, so với cùng kỳ, so với tháng trước, ngày đăng) hoặc số quá vô lý (≥ 25%): thà không có số còn hơn một số sai
/// được tự duyệt vào dự báo. Thập phân đọc bằng số nguyên, không qua <c>double</c>.
/// </summary>
public static class CpiReleaseParser
{
    private const int MaxPlausibleBps = 2500; // ≥ 25%/năm coi là đọc nhầm (ví dụ 45,7% thay vì 4,57%); khoảng tự duyệt còn do cấu hình quyết định

    private static readonly Dictionary<string, int> MonthWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["một"] = 1, ["hai"] = 2, ["ba"] = 3, ["tư"] = 4, ["bốn"] = 4, ["năm"] = 5, ["sáu"] = 6, ["bảy"] = 7, ["tám"] = 8, ["chín"] = 9,
        ["mười"] = 10, ["mười một"] = 11, ["mười hai"] = 12,
    };

    private static readonly Regex Tags = new(@"<(script|style)[^>]*>.*?</\1>|<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Average = new(@"Bình quân\s+[^.;]*?năm\s+(?<year>\d{4}),\s*CPI\s+(?<dir>tăng|giảm)\s+(?<n>\d+(?:[.,]\d+)?)\s*%\s+so với cùng kỳ", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MonthSentence = new(@"\(CPI\)\s+tháng\s+(?<month>mười một|mười hai|mười|một|hai|ba|tư|bốn|năm|sáu|bảy|tám|chín|\d{1,2})\s+(?<dirM>tăng|giảm)\s+(?<mom>\d+(?:[.,]\d+)?)\s*%\s+so với tháng trước[^.]*?(?<dirY>tăng|giảm)\s+(?<yoy>\d+(?:[.,]\d+)?)\s*%\s+so với cùng kỳ", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Issued = new(@"Ngày đăng:\s*(?<d>\d{1,2})/(?<m>\d{1,2})/(?<y>\d{4})", RegexOptions.Compiled);

    public static CpiRelease? TryParse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(html, " ")), " ");
        if (!text.Contains("Chỉ số giá tiêu dùng", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var average = Average.Match(text);
        var month = MonthSentence.Match(text);
        var issued = Issued.Match(text);
        if (!average.Success || !month.Success || !issued.Success)
        {
            return null;
        }

        var monthNumber = int.TryParse(month.Groups["month"].Value, out var digits) ? digits : MonthWords.GetValueOrDefault(month.Groups["month"].Value, 0);
        var averageBps = ToBps(average.Groups["n"].Value, average.Groups["dir"].Value);
        var yearOnYear = ToBps(month.Groups["yoy"].Value, month.Groups["dirY"].Value);
        var monthOnMonth = ToBps(month.Groups["mom"].Value, month.Groups["dirM"].Value);
        if (monthNumber is < 1 or > 12 || averageBps is null || yearOnYear is null || monthOnMonth is null)
        {
            return null;
        }

        if (!DateOnly.TryParse($"{issued.Groups["y"].Value}-{issued.Groups["m"].Value}-{issued.Groups["d"].Value}", out var publishedOn))
        {
            return null;
        }

        return new CpiRelease(int.Parse(average.Groups["year"].Value), monthNumber, averageBps.Value, yearOnYear.Value, monthOnMonth.Value, publishedOn);
    }

    /// <summary>"4,57" → 457; "giảm" đổi dấu. Quá 2 chữ số thập phân thì cắt (đủ cho số liệu thống kê); từ ±25% trở lên thì null.</summary>
    private static int? ToBps(string number, string direction)
    {
        var parts = number.Replace(',', '.').Split('.');
        if (!int.TryParse(parts[0], out var whole))
        {
            return null;
        }

        var fraction = parts.Length > 1 ? parts[1].PadRight(2, '0')[..2] : "00";
        if (!int.TryParse(fraction, out var hundredths))
        {
            return null;
        }

        var bps = whole * 100 + hundredths;
        if (bps >= MaxPlausibleBps)
        {
            return null;
        }

        return direction.Equals("giảm", StringComparison.OrdinalIgnoreCase) ? -bps : bps;
    }
}
