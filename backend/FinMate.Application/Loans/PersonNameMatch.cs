using System.Text;
using FinMate.Application.Forecasting;

namespace FinMate.Application.Loans;

/// <summary>
/// Khớp tên người giữa khoản vay và một câu / nội dung giao dịch (Phase 90, S3, Q15). MỘT chỗ cho cả chat và gợi ý từ thông báo.
/// <para>
/// Dấu xét theo CẢ CÂU: câu có chữ có dấu (người dùng gõ có dấu) thì so giữ dấu, nên "Hòa trả 500k" không khớp khoản "Hoa"; câu không có dấu nào
/// (thông báo ngân hàng, hoặc gõ không dấu) thì so bỏ dấu, nên "HOA TRA NO" khớp cả "Hoa" lẫn "Hòa". Chỉ so hai cái tên thì không thể vừa "Hoa ≠ Hòa"
/// vừa "ban binh = Bạn Bình".
/// </para>
/// </summary>
public static class PersonNameMatch
{
    private const int MinNameLength = 2;

    /// <summary>Câu có chữ có dấu tiếng Việt (kể cả đ) hay không.</summary>
    public static bool HasAccents(string? text)
        => !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Any(CounterpartyKey.IsAccented);

    /// <summary>
    /// Chữ thường, tách theo mọi ký tự không phải chữ / số. <paramref name="keepAccents"/> false: bỏ dấu và đ → d, bằng bảng của
    /// <see cref="CounterpartyKey"/> (dự án bật <c>InvariantGlobalization</c>, nên <c>Normalize(FormD)</c> không tách dấu).
    /// </summary>
    public static string[] Tokens(string? text, bool keepAccents)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lower = text.Trim().ToLowerInvariant();
        if (!keepAccents)
        {
            lower = new string(lower.Select(CounterpartyKey.FoldChar).ToArray());
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in lower)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Chat: hai tên là một người khi chữ của tên ngắn hơn nằm hết trong tên dài hơn ("An" trong "Bạn An", "Hoa" trong "chị Hoa"); khác nhau ở bất kỳ
    /// chữ nào thì không. <paramref name="sentence"/>: cả câu người dùng gõ, quyết định so giữ dấu hay bỏ dấu.
    /// </summary>
    public static bool SameName(string loanName, string saidName, string? sentence)
    {
        var keep = HasAccents(sentence ?? saidName);
        var a = Tokens(loanName, keep);
        var b = Tokens(saidName, keep);
        return a.Length > 0 && b.Length > 0 && (a.Length <= b.Length ? a : b).All((a.Length <= b.Length ? b : a).Contains);
    }

    /// <summary>
    /// Thông báo / nội dung giao dịch: các chữ của tên khoản, sau khi bỏ từ xưng hô đứng đầu ("bạn", "chị"…), là một dãy liền trong nội dung: "nam"
    /// khớp "NAM TRA NO", không khớp "NAMVIET". Tên dưới 2 ký tự không khớp.
    /// </summary>
    public static bool InText(string loanName, string? text, IReadOnlyCollection<string> honorifics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var keep = HasAccents(text);
        var name = Tokens(loanName, keep).ToList();
        var titles = honorifics.SelectMany(h => Tokens(h, keep)).ToHashSet(StringComparer.Ordinal);
        while (name.Count > 1 && titles.Contains(name[0]))
        {
            name.RemoveAt(0);
        }

        if (name.Count == 0 || string.Concat(name).Length < MinNameLength)
        {
            return false;
        }

        var words = Tokens(text, keep);
        for (var i = 0; i + name.Count <= words.Length; i++)
        {
            if (name.Select((w, k) => words[i + k] == w).All(x => x))
            {
                return true;
            }
        }

        return false;
    }
}
