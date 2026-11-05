using System.Net;
using System.Text.RegularExpressions;

namespace FinMate.Application.MarketData;

public sealed record PageLink(string Url, string Text);

/// <summary>
/// Lấy text và link từ HTML bằng thư viện chuẩn. Mọi biểu thức dùng <c>NonBacktracking</c>: HTML đến từ trang lạ, không được để một trang
/// viết khéo làm treo job. Đây không phải bộ phân tích HTML đầy đủ, nhưng đủ cho việc rút nội dung để đọc.
/// </summary>
public static class HtmlTextExtractor
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    /// <summary>Ngắn hơn mức này thì coi như không có nội dung: thường là trang cần JavaScript.</summary>
    public const int MinArticleLength = 200;
    private const int SolidArticleLength = 800;

    private static readonly Regex[] Removed = new[] { "script", "style", "noscript", "svg", "template", "nav", "header", "footer", "aside" }
        .Select(tag => new Regex($@"<{tag}\b[\s\S]*?</{tag}\s*>", Options))
        .Append(new Regex(@"<!--[\s\S]*?-->", Options))
        .ToArray();

    private static readonly Regex Article = new(@"<article\b[^>]*>([\s\S]*?)</article\s*>", Options);
    private static readonly Regex Main = new(@"<main\b[^>]*>([\s\S]*?)</main\s*>", Options);
    private static readonly Regex Body = new(@"<body\b[^>]*>([\s\S]*)</body\s*>", Options);
    private static readonly Regex BlockEnd = new(@"</?(?:p|div|li|ul|ol|br|h[1-6]|tr|table|section|article|blockquote)\b[^>]*>", Options);
    private static readonly Regex Tag = new(@"<[^>]*>", Options);
    private static readonly Regex Spaces = new(@"[ \t\r\f\v ]+", Options);
    private static readonly Regex BlankLines = new(@"\n(?:[ ]*\n)+", Options);
    private static readonly Regex Anchor = new(@"<a\b[^>]*?\bhref\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a\s*>", Options);
    private static readonly Regex[] DatePatterns =
    [
        new(@"<meta\b[^>]*(?:property|name|itemprop)\s*=\s*[""'](?:article:published_time|pubdate|datePublished)[""'][^>]*\bcontent\s*=\s*[""']([^""']+)[""']", Options),
        new(@"<meta\b[^>]*\bcontent\s*=\s*[""']([^""']+)[""'][^>]*(?:property|name|itemprop)\s*=\s*[""'](?:article:published_time|pubdate|datePublished)[""']", Options),
        new(@"""datePublished""\s*:\s*""([^""]+)""", Options),
        new(@"<time\b[^>]*\bdatetime\s*=\s*[""']([^""']+)[""']", Options),
    ];

    private static readonly Regex TitleTag = new(@"<title\b[^>]*>([\s\S]*?)</title\s*>", Options);

    /// <summary>Text của toàn trang, dùng để đọc bảng giá.</summary>
    public static string PageText(string html) => ToText(Clean(FirstGroup(Body, html) ?? html));

    /// <summary>
    /// Phần nội dung chính của một bài báo: khối <c>article</c> hoặc <c>main</c> dài nhất. Trang báo hay dùng <c>article</c> cho thẻ tin liên quan,
    /// nên khối đầu tiên chưa chắc là bài chính. Khối dài nhất vẫn ngắn thì lấy cả trang: thừa chữ còn hơn mất nội dung.
    /// </summary>
    public static string ArticleText(string html)
    {
        var cleaned = RemoveNoise(html);
        var longest = Article.Matches(cleaned).Concat(Main.Matches(cleaned))
            .Select(match => ToText(match.Groups[1].Value))
            .OrderByDescending(text => text.Length)
            .FirstOrDefault() ?? string.Empty;
        if (longest.Length >= SolidArticleLength)
        {
            return longest;
        }

        var page = ToText(FirstGroup(Body, cleaned) ?? cleaned);
        return page.Length > longest.Length ? page : longest;
    }

    public static string? Title(string html)
    {
        var title = FirstGroup(TitleTag, html);
        return title is null ? null : ToText(title);
    }

    /// <summary>Link trong trang, đã đổi thành URL tuyệt đối, chỉ giữ <c>https</c>.</summary>
    public static IReadOnlyList<PageLink> Links(string html, Uri baseUri)
    {
        var links = new List<PageLink>();
        foreach (Match match in Anchor.Matches(RemoveScripts(html)))
        {
            if (Uri.TryCreate(baseUri, WebUtility.HtmlDecode(match.Groups[1].Value).Trim(), out var target)
                && target.Scheme == Uri.UriSchemeHttps)
            {
                links.Add(new PageLink(new UriBuilder(target) { Fragment = string.Empty }.Uri.AbsoluteUri, ToText(match.Groups[2].Value)));
            }
        }

        return links;
    }

    private static string Clean(string html) => RemoveNoise(html);

    private static string RemoveScripts(string html)
    {
        foreach (var pattern in Removed.Take(2))
        {
            html = pattern.Replace(html, " ");
        }

        return html;
    }

    private static string RemoveNoise(string html)
    {
        foreach (var pattern in Removed)
        {
            html = pattern.Replace(html, " ");
        }

        return html;
    }

    private static string ToText(string html)
    {
        var withBreaks = BlockEnd.Replace(html, "\n");
        var decoded = RemoveControlChars(WebUtility.HtmlDecode(Tag.Replace(withBreaks, " ")));
        return BlankLines.Replace(Spaces.Replace(decoded, " "), "\n").Trim();
    }

    /// <summary>Thực thể <c>&amp;#0;</c> giải mã thành ký tự mà Postgres từ chối, và một tiêu đề như vậy không được làm hỏng cả lượt đọc.</summary>
    private static string RemoveControlChars(string text)
        => string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) && source[i] != '\n' ? ' ' : source[i];
            }
        });

    /// <summary>Ngày đăng trong thẻ meta, JSON-LD hoặc <c>time</c>. Null nếu không có, và bài như vậy không được tính là tin mới.</summary>
    public static DateTimeOffset? PublishedAt(string html)
    {
        foreach (var pattern in DatePatterns)
        {
            if (DateTimeOffset.TryParse(FirstGroup(pattern, html), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            {
                return date.ToUniversalTime();
            }
        }

        return null;
    }

    private static string? FirstGroup(Regex pattern, string input)
    {
        var match = pattern.Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }
}
