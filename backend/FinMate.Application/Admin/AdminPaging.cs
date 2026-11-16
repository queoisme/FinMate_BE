using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin;

/// <summary>
/// Giới hạn kích thước trang cho mọi danh sách admin.
///
/// Kẹp ở tầng handler chứ không phải ở controller: controller nhận số từ query string, nhưng
/// handler là thứ duy nhất mọi đường gọi đều đi qua — kẹp ở đây thì một endpoint mới thêm sau
/// này không thể vô tình mở cửa cho <c>?limit=1000000</c>.
/// </summary>
public static class AdminPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static int Clamp(int limit)
        => limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    /// <summary>Offset không âm, limit đã kẹp. Mọi danh sách kiểu offset đi qua đây để không endpoint nào tự quy ước.</summary>
    public static (int Offset, int Limit) Normalize(int offset, int limit)
        => (Math.Max(offset, 0), Clamp(limit));

    /// <summary>
    /// Chữ thường, bỏ dấu tiếng Việt (đ → d): gõ "an uong" tìm được "Ăn uống". Dùng cho tìm kiếm trên danh mục nhỏ đã nạp vào bộ nhớ;
    /// bảng lớn tìm bằng ILIKE ở DB (xem <see cref="EscapeLike"/>).
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Không dùng string.Normalize: backend bật InvariantGlobalization (Directory.Build.props) nên Normalize không tách dấu chữ không-ASCII.
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (c is >= '̀' and <= 'ͯ')
            {
                continue;   // dấu rời (chữ đã ở dạng tổ hợp NFD)
            }

            sb.Append(VietnameseFold.TryGetValue(c, out var plain) ? plain : char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static readonly Dictionary<char, char> VietnameseFold = BuildVietnameseFold();

    private static Dictionary<char, char> BuildVietnameseFold()
    {
        var map = new Dictionary<char, char>();
        void Add(char plain, string accented)
        {
            foreach (var c in accented)
            {
                map[c] = plain;
                map[char.ToUpperInvariant(c)] = plain;
            }
        }

        Add('a', "àáạảãâầấậẩẫăằắặẳẵ");
        Add('e', "èéẹẻẽêềếệểễ");
        Add('i', "ìíịỉĩ");
        Add('o', "òóọỏõôồốộổỗơờớợởỡ");
        Add('u', "ùúụủũưừứựửữ");
        Add('y', "ỳýỵỷỹ");
        Add('d', "đ");
        return map;
    }

    /// <summary>Không có từ khóa thì khớp tất cả; có thì chỉ cần MỘT trường chứa nó (không phân biệt hoa thường và dấu).</summary>
    public static bool Matches(string? search, params string?[] fields)
    {
        var needle = Fold(search);
        return needle.Length == 0 || fields.Any(f => Fold(f).Contains(needle, StringComparison.Ordinal));
    }

    /// <summary>Cắt một trang từ danh sách ĐÃ SẮP XẾP ỔN ĐỊNH (cùng bộ lọc luôn ra cùng thứ tự giữa các trang).</summary>
    public static PagedResult<T> Page<T>(IEnumerable<T> ordered, int offset, int limit)
    {
        var (skip, take) = Normalize(offset, limit);
        var all = ordered as IReadOnlyList<T> ?? ordered.ToList();
        return new PagedResult<T>(all.Skip(skip).Take(take).ToList(), all.Count, skip, take);
    }

    /// <summary>Chuỗi để nhét vào <c>EF.Functions.ILike(col, $"%{...}%", "\\")</c>: người dùng gõ % hoặc _ phải được hiểu là chữ, không phải ký tự đại diện.</summary>
    public static string EscapeLike(string text)
        => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
