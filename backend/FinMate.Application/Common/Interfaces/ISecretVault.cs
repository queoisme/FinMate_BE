namespace FinMate.Application.Common.Interfaces;

/// <summary>Trạng thái một key để hiện trên trang quản trị. Không bao giờ chứa giá trị key.</summary>
/// <param name="Multiline">Giá trị nhiều dòng (file JSON) nên trang quản trị dùng ô nhiều dòng thay vì ô mật khẩu.</param>
/// <param name="Source"><c>db</c> (admin đã nhập), <c>env</c> (biến môi trường) hoặc <c>none</c>.</param>
public sealed record SecretStatus(string Name, string Title, string Source, bool Configured, string? Last4, DateTimeOffset? UpdatedAt, bool Multiline = false);

/// <summary>
/// Kho key bên thứ ba của backend: key admin nhập (mã hóa trong DB) được ưu tiên, không có thì rơi về biến môi trường như trước khi có kho, nên không có gì ngừng chạy lúc chuyển.
/// Giá trị rõ chỉ trả cho code cần gọi nhà cung cấp, và không ra khỏi tiến trình qua bất kỳ API nào.
/// </summary>
public interface ISecretVault
{
    /// <summary>Đã có khóa chủ hợp lệ để lưu key vào DB chưa. Chưa có thì vẫn đọc được key ở biến môi trường.</summary>
    bool CanStore { get; }

    /// <summary>Giá trị đang dùng của <paramref name="name"/> (DB trước, biến môi trường sau), hoặc null nếu chưa có.</summary>
    Task<string?> GetAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken ct = default);

    /// <summary>Mã hóa và ghi đè key. Ném lỗi nghiệp vụ nếu tên lạ, giá trị sai định dạng hoặc chưa có khóa chủ.</summary>
    Task<SecretStatus> SaveAsync(string name, string value, Guid adminId, CancellationToken ct = default);

    /// <summary>Xóa key khỏi DB; sau đó dùng biến môi trường nếu có.</summary>
    Task<SecretStatus> DeleteAsync(string name, CancellationToken ct = default);
}

/// <summary>Thử một key bằng lệnh gọi chỉ-đọc tới nhà cung cấp, không gửi gì thật và không ghi gì.</summary>
public interface ISecretTester
{
    Task<SecretTestResult> TestAsync(string name, CancellationToken ct = default);
}

public sealed record SecretTestResult(bool Ok, string Detail);
