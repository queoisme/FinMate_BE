using System.Text;

namespace FinMate.Application.Community;

/// <summary>
/// Ghép dấu tổ hợp tiếng Việt thành ký tự dựng sẵn, để "ê" viết thành hai mã vẫn khớp từ cấm. Tự làm bằng bảng vì solution bật <c>InvariantGlobalization</c>, nên không dùng được <c>string.Normalize</c>.
/// Trả về chữ thường.
/// </summary>
public static class VietnameseText
{
    private const string Tones = "̣̀́̉̃";

    private static readonly Dictionary<(char Base, char Mark), char> Shapes = new()
    {
        [('a', '̂')] = 'â',
        [('a', '̆')] = 'ă',
        [('e', '̂')] = 'ê',
        [('o', '̂')] = 'ô',
        [('o', '̛')] = 'ơ',
        [('u', '̛')] = 'ư',
    };

    // Mỗi chuỗi theo đúng thứ tự dấu trong Tones.
    private static readonly Dictionary<char, string> Toned = new()
    {
        ['a'] = "àáảãạ",
        ['ă'] = "ằắẳẵặ",
        ['â'] = "ầấẩẫậ",
        ['e'] = "èéẻẽẹ",
        ['ê'] = "ềếểễệ",
        ['i'] = "ìíỉĩị",
        ['o'] = "òóỏõọ",
        ['ô'] = "ồốổỗộ",
        ['ơ'] = "ờớởỡợ",
        ['u'] = "ùúủũụ",
        ['ư'] = "ừứửữự",
        ['y'] = "ỳýỷỹỵ",
    };

    public static string ComposeLower(string text)
    {
        var lowered = text.ToLowerInvariant();
        if (!lowered.Any(IsMark))
        {
            return lowered;
        }

        var builder = new StringBuilder(lowered.Length);
        var i = 0;
        while (i < lowered.Length)
        {
            var current = lowered[i++];
            var marksStart = i;
            while (i < lowered.Length && IsMark(lowered[i]))
            {
                i++;
            }

            builder.Append(Compose(current, lowered.AsSpan(marksStart, i - marksStart)));
        }

        return builder.ToString();
    }

    /// <summary>Dấu hình (mũ, móc) áp trước dấu thanh, bất kể thứ tự trong văn bản: dạng tách chuẩn của "ậ" đặt dấu nặng trước dấu mũ.</summary>
    private static char Compose(char letter, ReadOnlySpan<char> marks)
    {
        var result = letter;
        foreach (var mark in marks)
        {
            result = Shapes.GetValueOrDefault((result, mark), result);
        }

        foreach (var mark in marks)
        {
            var tone = Tones.IndexOf(mark);
            if (tone >= 0 && Toned.TryGetValue(result, out var forms))
            {
                result = forms[tone];
            }
        }

        return result;
    }

    private static bool IsMark(char c) => c is >= '̀' and <= 'ͯ';
}
