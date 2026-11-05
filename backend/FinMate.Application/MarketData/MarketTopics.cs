using System.Text;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

/// <summary>
/// So khớp từ khóa theo cả từ, để "evn" không khớp "seven". Từ khóa kết thúc bằng <c>*</c> khớp phần đầu của từ.
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

    /// <summary>Cho giao dịch: không phân biệt dấu, vì ngân hàng thường viết không dấu. Các cụm trong <paramref name="excludes"/> bị bỏ trước khi so.</summary>
    public static bool MatchesTransaction(string text, IEnumerable<string> keywords, IEnumerable<string> excludes)
    {
        var normalized = Normalize(text);
        foreach (var exclude in excludes)
        {
            normalized = normalized.Replace(Normalize(exclude), " ", StringComparison.Ordinal);
        }

        return keywords.Any(keyword => ContainsWord(normalized, Normalize(keyword)));
    }

    /// <summary>Cho tin bài: giữ dấu, vì bỏ dấu thì "chuyên gia" và "giá" trùng nhau, và bài không liên quan sẽ tốn lượt đọc AI.</summary>
    public static bool MatchesNews(string text, IEnumerable<string> keywords)
    {
        var lowered = text.ToLowerInvariant();
        return keywords.Any(keyword => ContainsWord(lowered, keyword.ToLowerInvariant()));
    }

    private static bool ContainsWord(string text, string word)
    {
        var prefixOnly = word.EndsWith('*');
        var target = prefixOnly ? word[..^1] : word;
        if (target.Length == 0)
        {
            return false;
        }

        for (var at = text.IndexOf(target, StringComparison.Ordinal); at >= 0; at = text.IndexOf(target, at + 1, StringComparison.Ordinal))
        {
            var end = at + target.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (prefixOnly || end == text.Length || !char.IsLetterOrDigit(text[end])))
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
        return MarketKeywords.MatchesNews(text, GeneralKeywords)
            || settings.CostTypes.Any(type => type.Enabled && MarketKeywords.MatchesNews(text, type.NewsKeywords));
    }
}
