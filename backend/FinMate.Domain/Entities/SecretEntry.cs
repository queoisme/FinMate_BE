namespace FinMate.Domain.Entities;

/// <summary>
/// Một key bên thứ ba của BACKEND (Brevo, Google OAuth, Firebase) do admin nhập qua trang quản trị, lưu đã mã hóa AES-256-GCM bằng khóa chủ ở biến môi trường <c>SECRETS_MASTER_KEY</c>.
/// Không có cột nào chứa giá trị rõ: <see cref="Last4"/> chỉ để admin nhận ra đó là key nào. Key của AI Service và OCR Service nằm ở kho riêng của AI Service, không ở đây.
/// </summary>
public class SecretEntry
{
    /// <summary>Tên key (<c>brevo_api_key</c>...). Cũng là dữ liệu xác thực kèm (AAD) khi mã hóa: chép ciphertext sang dòng tên khác thì giải mã thất bại.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Bản mã nối với thẻ xác thực 16 byte của AES-GCM.</summary>
    public byte[] Ciphertext { get; set; } = [];

    /// <summary>12 byte ngẫu nhiên, mỗi lần lưu một giá trị mới.</summary>
    public byte[] Nonce { get; set; } = [];

    /// <summary>Phiên bản khóa chủ đã dùng, để sau này đổi khóa mà vẫn đọc được dòng cũ.</summary>
    public short KeyVersion { get; set; } = 1;

    public string Last4 { get; set; } = string.Empty;

    /// <summary>Admin đổi gần nhất. Không có khóa ngoại: xóa cứng tài khoản không được làm mất dấu vết đổi key.</summary>
    public Guid UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
