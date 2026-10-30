namespace FinMate.Domain.Entities;

/// <summary>
/// Một ngân hàng có thể là ĐÍCH hoặc NGUỒN của chuyển khoản qua mã VietQR, kèm mã để mở đúng app ngân hàng qua liên kết <c>dl.vietqr.io</c>. Là danh mục dùng chung (không thuộc người dùng nào),
/// do seeder nạp và admin sửa sau; app lấy danh sách từ đây thay vì giữ bản cứng.
/// </summary>
public class VietQrBank
{
    /// <summary>Mã BIN NAPAS 6 chữ số (khóa tự nhiên): thứ có trong mã QR của người nhận.</summary>
    public string Bin { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Mã app theo <c>dl.vietqr.io</c> (<c>mb</c>, <c>icb</c>...). Dùng cho cả tham số <c>app</c> (ngân hàng người GỬI) và <c>ba</c> (ngân hàng người NHẬN).</summary>
    public string DeeplinkAppCode { get; set; } = string.Empty;

    /// <summary>
    /// <c>provider_configs.provider_key</c> của cùng ngân hàng, nếu FinMate đọc được thông báo của nó. Đây là cầu nối từ "app ngân hàng người dùng gửi tiền" sang "thông báo ngân hàng về": khớp giao dịch
    /// chỉ chạy được với ngân hàng có giá trị này. Không có khóa ngoại: <c>provider_key</c> là khóa nghiệp vụ admin có thể thêm sau, không phải khóa chính.
    /// </summary>
    public string? ProviderKey { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
