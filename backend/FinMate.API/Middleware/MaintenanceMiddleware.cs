using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.API.Middleware;

/// <summary>
/// Khi bật chế độ bảo trì, mọi API trả 503 trừ: cấu hình công khai (để app biết đang bảo trì), đăng nhập, kiểm tra sống và tài liệu. ADMIN vẫn dùng được toàn bộ để kiểm tra và tắt bảo trì.
/// Phía app đã chặn bằng popup; chặn ở máy chủ là để bản app cũ không biết popup và hàng đợi đồng bộ không dồn vào hệ thống đang được sửa.
/// Đứng SAU xác thực vì cần biết người gọi có phải Admin không.
/// </summary>
public class MaintenanceMiddleware
{
    public const string ErrorCode = "MAINTENANCE";

    private static readonly string[] AlwaysAllowed = ["/api/v1/app-config", "/api/v1/auth/", "/health", "/hangfire", "/swagger"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISystemConfigProvider provider)
    {
        var maintenance = (await provider.GetAsync(context.RequestAborted)).Document.Maintenance;
        if (!maintenance.Enabled || IsAlwaysAllowed(context.Request.Path) || context.User.IsInRole(nameof(UserRole.Admin)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        if (maintenance.EstimatedEndAt is { } end && end > DateTimeOffset.UtcNow)
        {
            context.Response.Headers.RetryAfter = ((int)(end - DateTimeOffset.UtcNow).TotalSeconds).ToString();
        }

        var body = ApiResponse<object>.Fail(new ApiError(ErrorCode, maintenance.Message, null));
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }

    private static bool IsAlwaysAllowed(PathString path)
        => AlwaysAllowed.Any(prefix => path.StartsWithSegments(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
}
