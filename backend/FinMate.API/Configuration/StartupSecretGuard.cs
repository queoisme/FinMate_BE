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
    private const string Placeholder = "change-me";

    /// <summary>Những biến mà giá trị mẫu là rủi ro bảo mật thật, không chỉ là cấu hình sai.</summary>
    public static readonly string[] GuardedKeys =
        ["JWT_SECRET", "AI_SERVICE_API_KEY", "ADMIN_SEED_PASSWORD", "HANGFIRE_DASHBOARD_PASS",
         "BREVO_API_KEY"];

    /// <summary>Tên các biến còn giữ giá trị mẫu, theo đúng thứ tự khai báo.</summary>
    public static IReadOnlyList<string> FindUnchanged(IConfiguration configuration)
        => GuardedKeys
            .Where(key => configuration[key]?.Contains(Placeholder, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

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
