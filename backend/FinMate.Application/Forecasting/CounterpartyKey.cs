using System.Text;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Khóa so khớp người nhận/người trả của giao dịch: chữ thường, bỏ dấu tiếng Việt, gộp khoảng trắng. "Netflix ", "NETFLIX" và "netflix"
/// là một người nhận. Gấp dấu bằng bảng ký tự chứ không dùng <c>string.Normalize</c>: solution bật <c>InvariantGlobalization</c>.
/// </summary>
public static class CounterpartyKey
{
    public const int MaxLength = 120;

    private const string Accented =
        "àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ";

    private const string Plain =
        "aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd";

    private static readonly Dictionary<char, char> Fold = Accented.Zip(Plain).ToDictionary(p => p.First, p => p.Second);

    /// <summary>Null khi không có tên dùng được (trống hoặc chỉ toàn ký hiệu).</summary>
    public static string? From(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var raw in name.Trim().ToLowerInvariant())
        {
            var c = Fold.GetValueOrDefault(raw, raw);
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(c);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        if (builder.Length == 0)
        {
            return null;
        }

        return builder.Length <= MaxLength ? builder.ToString() : builder.ToString(0, MaxLength).TrimEnd();
    }
}
