using System.Security.Cryptography;

namespace FinMate.Domain.Entities;

/// <summary>
/// Mã FinMate ID của tài khoản, dạng <c>FM-XXXXXXXX</c>: để người khác tìm và mời, không dùng để đăng nhập. Tự sinh, duy nhất, chỉ đọc.
/// Bảng chữ không có I, O (dễ nhầm với 1, 0), 32 ký tự, 8 ký tự: ~10^12 mã nên đụng nhau gần như không xảy ra; nếu có thì chỉ mục duy nhất làm lượt đăng ký đó lỗi.
/// </summary>
public static class FinMateIds
{
    public const string Prefix = "FM-";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    public static string New()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Prefix + new string(chars);
    }

    /// <summary>Chấp nhận cả khi người dùng gõ thường hoặc thiếu tiền tố "FM-"; trả mã chuẩn hoá.</summary>
    public static bool TryNormalize(string? input, out string id)
    {
        id = string.Empty;
        var value = input?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            value = value[Prefix.Length..];
        }

        if (value.Length != Length || value.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            return false;
        }

        id = Prefix + value;
        return true;
    }
}
