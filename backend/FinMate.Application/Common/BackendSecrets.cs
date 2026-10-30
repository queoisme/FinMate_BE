using System.Text.Json;

namespace FinMate.Application.Common;

/// <summary>
/// Danh mục key bên thứ ba mà BACKEND dùng và quản trị được. Thêm key mới = thêm một dòng ở đây (cùng chỗ dùng nó), không cần bảng hay endpoint mới.
/// Key nội bộ giữa các dịch vụ (<c>JWT_SECRET</c>, <c>AI_SERVICE_API_KEY</c>...) cố ý KHÔNG có ở đây: đổi chúng làm các dịch vụ không nhận ra nhau, nên chỉ đổi bằng biến môi trường.
/// </summary>
public static class BackendSecrets
{
    public const string BrevoApiKey = "brevo_api_key";
    public const string GoogleClientSecret = "google_client_secret";
    public const string FcmCredentialsJson = "fcm_credentials_json";

    public const int MaxLength = 8192;

    public sealed record Definition(string Name, string Title, string EnvVar, Func<string, string?> Validate, bool Multiline = false);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(BrevoApiKey, "Brevo API key (gửi email OTP)", "BREVO_API_KEY", ValidateToken),
        new(GoogleClientSecret, "Google OAuth client secret (đăng nhập Google)", "GOOGLE_CLIENT_SECRET", ValidateToken),
        new(FcmCredentialsJson, "Firebase service account JSON (thông báo đẩy FCM)", "FCM_CREDENTIALS_JSON", ValidateServiceAccount, Multiline: true),
    ];

    public static Definition? Find(string name) => All.FirstOrDefault(d => d.Name == name);

    /// <summary>Trả lý do từ chối (tiếng Việt, không chứa giá trị) hoặc null nếu hợp lệ.</summary>
    private static string? ValidateToken(string value)
        => value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "Key không được chứa khoảng trắng hay ký tự điều khiển." : null;

    private static string? ValidateServiceAccount(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.GetString() != "service_account"
                || !root.TryGetProperty("client_email", out var email) || string.IsNullOrWhiteSpace(email.GetString())
                || !root.TryGetProperty("private_key", out var key) || string.IsNullOrWhiteSpace(key.GetString()))
            {
                return "Đây phải là file JSON service account của Firebase (có type, client_email, private_key).";
            }

            return null;
        }
        catch (JsonException)
        {
            return "Nội dung không phải JSON hợp lệ. Dán nguyên file service account của Firebase.";
        }
    }
}
