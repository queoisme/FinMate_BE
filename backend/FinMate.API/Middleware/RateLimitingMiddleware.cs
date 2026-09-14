using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Middleware;

/// <summary>
/// Cấu hình rate limiter sẵn có của ASP.NET Core.
///
/// Cho tới Phase 14 hai policy dưới đây được ĐỊNH NGHĨA nhưng không gắn vào endpoint nào —
/// không <c>[EnableRateLimiting]</c>, không <c>RequireRateLimiting()</c>, không
/// <c>GlobalLimiter</c>. <c>app.UseRateLimiter()</c> vẫn chạy nhưng không giới hạn gì cả, nên
/// mức 10 lần/phút cho đăng nhập hoàn toàn không có tác dụng chống dò mật khẩu.
/// </summary>
public static class RateLimitingMiddleware
{
    public const string AuthPolicy = "auth";

    private const int DefaultPermitPerMinute = 120;
    private const int AuthPermitPerMinute = 10;

    public static IServiceCollection AddFinMateRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Đọc từ cấu hình: ngưỡng phải chỉnh được mà không phải build lại, và bộ test tích hợp
        // cần nới rộng ra để 449 test không tự chặn lẫn nhau.
        var defaultPermit = Read(configuration, "RATE_LIMIT_DEFAULT_PER_MINUTE", DefaultPermitPerMinute);
        var authPermit = Read(configuration, "RATE_LIMIT_AUTH_PER_MINUTE", AuthPermitPerMinute);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // GlobalLimiter chứ KHÔNG phải một policy gắn qua RequireRateLimiting():
            // metadata của endpoint thắng metadata do convention thêm vào, nên gắn policy mặc
            // định lên MapControllers() sẽ ĐÈ MẤT [EnableRateLimiting(AuthPolicy)] trên các
            // endpoint đăng nhập — đo thực tế cho thấy /auth/login chạy ở mức 120 thay vì 10.
            // GlobalLimiter thì cộng dồn: mọi request đều qua nó, endpoint nào có policy riêng
            // thì phải qua cả hai, và một controller thêm sau này không thể lọt lưới.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKeyFor(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = defaultPermit,
                    }));

            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // Đăng nhập/đăng ký là ẩn danh nên chỉ còn IP để phân vùng.
                    partitionKey: ClientIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = authPermit,
                    }));
        });

        return services;
    }

    /// <summary>
    /// Đã đăng nhập thì phân vùng theo NGƯỜI DÙNG, chưa thì theo IP.
    ///
    /// Phân vùng theo IP cho request đã đăng nhập là sai ở cả hai đầu: cả một trường học sau
    /// một NAT dùng chung hạn mức, trong khi một tài khoản đổi mạng liên tục thì thoát. Nó còn
    /// đánh nhầm đúng thứ đang cần hỗ trợ — hàng đợi offline đồng bộ một loạt request của CÙNG
    /// một người (docx mục "Mạng Offline"), và ngưỡng theo người dùng mới là ngưỡng nói đúng
    /// điều ta muốn giới hạn.
    ///
    /// Đòi <c>UseRateLimiter()</c> phải chạy SAU <c>UseAuthentication()</c>, nếu không
    /// <c>context.User</c> còn rỗng và mọi request đều rơi về nhánh IP.
    /// </summary>
    private static string PartitionKeyFor(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? $"ip:{ClientIp(context)}" : $"user:{userId}";
    }

    private static string ClientIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static int Read(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
}
