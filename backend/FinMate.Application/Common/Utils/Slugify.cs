using System.Text;
using System.Text.RegularExpressions;

namespace FinMate.Application.Common.Utils;

// Không dùng string.Normalize/CharUnicodeInfo (ICU) để bỏ dấu tiếng Việt — Directory.Build.props
// bật InvariantGlobalization=true toàn solution, khiến các API đó không đáng tin cậy (ICU-less).
// Thay vào đó dùng bảng ánh xạ ký tự tường minh, không phụ thuộc globalization.
public static partial class Slugify
{
    private static readonly Dictionary<char, char> CharMap = BuildCharMap();

    private static Dictionary<char, char> BuildCharMap()
    {
        var map = new Dictionary<char, char>();

        void AddGroup(string variants, char target)
        {
            foreach (var c in variants)
            {
                map[c] = target;
            }
        }

        AddGroup("aàáảãạăằắẳẵặâầấẩẫậAÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬ", 'a');
        AddGroup("eèéẻẽẹêềếểễệEÈÉẺẼẸÊỀẾỂỄỆ", 'e');
        AddGroup("iìíỉĩịIÌÍỈĨỊ", 'i');
        AddGroup("oòóỏõọôồốổỗộơờớởỡợOÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢ", 'o');
        AddGroup("uùúủũụưừứửữựUÙÚỦŨỤƯỪỨỬỮỰ", 'u');
        AddGroup("yỳýỷỹỵYỲÝỶỸỴ", 'y');
        AddGroup("dđDĐ", 'd');

        return map;
    }

    public static string ToSlug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (CharMap.TryGetValue(c, out var mapped))
            {
                builder.Append(mapped);
            }
            else if (c is >= 'A' and <= 'Z')
            {
                builder.Append((char)(c + 32));
            }
            else
            {
                builder.Append(c);
            }
        }

        var slug = NonAlphaNumericRegex().Replace(builder.ToString(), "-");
        slug = HyphenRunRegex().Replace(slug, "-").Trim('-');

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("-+")]
    private static partial Regex HyphenRunRegex();
}
