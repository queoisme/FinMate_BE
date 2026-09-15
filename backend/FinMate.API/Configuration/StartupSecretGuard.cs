using Microsoft.Extensions.Configuration;

namespace FinMate.API.Configuration;

/// <summary>
/// Chặn khởi động khi bí mật còn là giá trị mẫu.
///
/// Giá trị "change-me..." nằm công khai trong <c>.env.example</c> ĐÃ COMMIT, nên một
/// <c>JWT_SECRET</c> còn nguyên nghĩa là ai đọc repo cũng tự ký được token admin. Hỏng kiểu
/// này hoàn toàn im lặng — mọi thứ vẫn chạy bình thường — nên phải bắt lúc khởi động.
/// </summary>
public static class StartupSecretGuard
{
    private const string Placeholder = "changeme";

    /// <summary>Những biến mà giá trị mẫu là rủi ro bảo mật thật, không chỉ là cấu hình sai.</summary>
    public static readonly string[] GuardedKeys =
        ["JWT_SECRET", "AI_SERVICE_API_KEY", "ADMIN_SEED_PASSWORD", "HANGFIRE_DASHBOARD_PASS",
         "BREVO_API_KEY", "GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET"];

    /// <summary>Tên các biến còn giữ giá trị mẫu, theo đúng thứ tự khai báo.</summary>
    public static IReadOnlyList<string> FindUnchanged(IConfiguration configuration)
        => GuardedKeys.Where(key => IsPlaceholder(configuration[key])).ToList();

    /// <summary>
    /// So sau khi bỏ dấu nối và khoảng trắng.
    ///
    /// So chuỗi thô là chưa đủ: `.env.example` dùng CẢ HAI kiểu viết — `change-me-local-dev`
    /// lẫn `ChangeMe123!` cho `ADMIN_SEED_PASSWORD`. Bản đầu chỉ tìm "change-me" nên bỏ lọt
    /// đúng cái thứ hai, tức là deploy được lên production với một tài khoản admin có mật khẩu
    /// nằm công khai trong repo, mà chốt chặn không nói gì.
    /// </summary>
    internal static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Contains(Placeholder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ném nếu còn bí mật mẫu. Không gọi ở Development: máy dev dùng đúng những giá trị đó và
    /// chặn ở đó chỉ tổ cản việc.
    /// </summary>
    public static void ThrowIfPlaceholdersRemain(IConfiguration configuration, string environmentName)
    {
        var unchanged = FindUnchanged(configuration);
        if (unchanged.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start in {environmentName}: these still hold the placeholder value "
            + $"from .env.example — {string.Join(", ", unchanged)}.");
    }
}
