using System.Text.RegularExpressions;
using FinMate.Application.MarketData;

namespace FinMate.Application.Community;

/// <summary>
/// Chặn những thứ dễ gây hại nhất trong nội dung công khai: đường dẫn, số điện thoại, email, chuỗi số dài (số tài khoản, CCCD) và từ cấm. Đây chỉ là lớp giảm bớt,
/// viết lách kiểu "không chín một hai" vẫn lọt; phần còn lại nhờ báo cáo và nhân viên.
/// </summary>
public static partial class CommunityContentPolicy
{
    [GeneratedRegex(@"https?://|www\.|\b[\p{L}\d-]+\.(com|vn|net|org|io|me|info|xyz|link|top|shop|store|site|online|app|co)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Link();

    [GeneratedRegex(@"[\p{L}\d._%+-]+@[\p{L}\d-]+\.[\p{L}\d.-]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Email();

    // Không có chữ số, dấu chấm hay dấu phẩy ngay trước: "2.000.000.000" là số tiền, không phải số điện thoại.
    [GeneratedRegex(@"(?<![\d.,])(?:\+?84|0)(?:[\s.\-]*\d){9}(?!\d)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Phone();

    [GeneratedRegex(@"\d{9,}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex LongNumber();

    /// <summary>Có chứa thứ không được đăng hay không. Quá thời gian kiểm tra thì coi như không đạt, vì không kiểm được thì không nên đăng.</summary>
    public static bool IsAllowed(string? text, IReadOnlyList<string> bannedWords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            return !HasContact(text) && !MarketKeywords.MatchesNews(VietnameseText.ComposeLower(text), bannedWords);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Riêng cho tên hiển thị: không xét từ cấm trên giả định tên là của chính họ, nhưng tên sắp hiện công khai nên phải sạch thông tin liên hệ.</summary>
    public static bool IsDisplayNameAllowed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            return !HasContact(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool HasContact(string text)
        => Link().IsMatch(text) || Email().IsMatch(text) || Phone().IsMatch(text) || LongNumber().IsMatch(text);
}
