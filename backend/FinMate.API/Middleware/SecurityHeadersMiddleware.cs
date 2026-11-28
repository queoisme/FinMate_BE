namespace FinMate.API.Middleware;

/// <summary>
/// Header bảo mật tối thiểu cho mọi response của API (kể cả 401/500). API trả JSON nên không đặt
/// CSP ở đây; CSP thuộc về nơi phục vụ web admin (xem <c>vercel.json</c> của FinMate_Admin).
/// Đặt trong <c>OnStarting</c> để lỗi do middleware phía sau ném ra vẫn mang header.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");

            // Dữ liệu quản trị/vận hành không được nằm lại trong cache của trình duyệt hay proxy dùng chung.
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api/v1/admin") || path.StartsWithSegments("/api/v1/ops"))
            {
                headers.TryAdd("Cache-Control", "no-store");
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
