using System.Text;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

/// <summary>
/// So khớp từ khóa theo cả từ, không phân biệt hoa thường và dấu, vì thông báo ngân hàng thường viết "DO XANG". Theo cả từ để "evn" không khớp "seven".
/// Bảng ký tự viết tay vì <c>InvariantGlobalization</c> bật toàn solution nên không dựa vào <c>string.Normalize</c> được.
/// </summary>
public static class MarketKeywords
{
    private static readonly Dictionary<char, char> Fold = BuildFold();

    private static Dictionary<char, char> BuildFold()
    {
        var map = new Dictionary<char, char>();
        foreach (var (variants, target) in new[]
        {
            ("àáảãạăằắẳẵặâầấẩẫậ", 'a'),
            ("èéẻẽẹêềếểễệ", 'e'),
            ("ìíỉĩị", 'i'),
            ("òóỏõọôồốổỗộơờớởỡợ", 'o'),
            ("ùúủũụưừứửữự", 'u'),
            ("ỳýỷỹỵ", 'y'),
            ("đ", 'd'),
        })
        {
            foreach (var c in variants)
            {
                map[c] = target;
                map[char.ToUpperInvariant(c)] = target;
            }
        }

        return map;
    }

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // Bỏ dấu tổ hợp, phòng khi văn bản viết ở dạng tách dấu.
            if (c is >= '\u0300' and <= '\u036F')
            {
                continue;
            }

            builder.Append(Fold.TryGetValue(c, out var mapped) ? mapped : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        var normalized = Normalize(text);
        return keywords.Any(keyword => ContainsWord(normalized, Normalize(keyword)));
    }

    private static bool ContainsWord(string text, string word)
    {
        if (word.Length == 0)
        {
            return false;
        }

        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var end = at + word.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Lọc bài trước khi tốn lượt đọc: một RSS có 60 tin nhưng chỉ vài tin nói về giá cả ảnh hưởng tới chi tiêu.</summary>
public static class MarketTopics
{
    /// <summary>Chủ đề của tin giá cả chưa có loại chi phí tương ứng. Được lưu nhưng không tạo cảnh báo.</summary>
    public const string Other = "other";

    private static readonly string[] GeneralKeywords = ["giá tiêu dùng", "cpi", "lạm phát"];

    public static bool IsRelevant(MarketDataSettings settings, string? title, string? summary = null)
    {
        var text = $"{title} {summary}";
        return MarketKeywords.ContainsAny(text, GeneralKeywords)
            || settings.CostTypes.Any(type => type.Enabled && MarketKeywords.ContainsAny(text, type.NewsKeywords));
    }
}
