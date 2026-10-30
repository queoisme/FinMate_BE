using System.Security.Cryptography;
using System.Text;

namespace FinMate.Infrastructure.Security;

/// <summary>
/// AES-256-GCM cho key bên thứ ba. Khóa chủ là 32 byte ngẫu nhiên viết base64 (chuẩn hoặc url-safe) ở biến môi trường <c>SECRETS_MASTER_KEY</c>; không có khóa hợp lệ thì
/// <see cref="Available"/> là false và không mã hóa được gì (key ở biến môi trường vẫn dùng bình thường). Tên key làm dữ liệu xác thực kèm (AAD): chép bản mã của key này sang dòng
/// key khác thì giải mã thất bại thay vì trả nhầm key. Mỗi lần mã hóa dùng nonce 12 byte ngẫu nhiên mới.
/// </summary>
public sealed class SecretCipher
{
    public const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;

    public SecretCipher(string? masterKey)
    {
        _key = TryParse(masterKey);
    }

    public bool Available => _key is not null;

    private static byte[]? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normal = text.Trim().Replace('-', '+').Replace('_', '/');
        normal = normal.PadRight(normal.Length + (4 - normal.Length % 4) % 4, '=');
        try
        {
            var bytes = Convert.FromBase64String(normal);
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Trả (bản mã + thẻ xác thực, nonce). Ném <see cref="InvalidOperationException"/> nếu chưa có khóa chủ.</summary>
    public (byte[] Ciphertext, byte[] Nonce) Encrypt(string name, string plaintext)
    {
        var key = _key ?? throw new InvalidOperationException("SECRETS_MASTER_KEY chưa được cấu hình.");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(name));
        return ([.. cipher, .. tag], nonce);
    }

    /// <summary>Giải mã; trả null nếu sai khóa, bị sửa hoặc bị chép sang tên khác (không phân biệt, không ném).</summary>
    public string? Decrypt(string name, byte[] ciphertext, byte[] nonce)
    {
        if (_key is null || ciphertext.Length < TagSize || nonce.Length != NonceSize)
        {
            return null;
        }

        var split = ciphertext.Length - TagSize;
        var plain = new byte[split];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext.AsSpan(0, split), ciphertext.AsSpan(split), plain, Encoding.UTF8.GetBytes(name));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
