using System.Web;
using FinMate.Application.Auth.Commands;
using FinMate.Application.Auth.GoogleOAuth;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FinMate.API.Middleware;

namespace FinMate.API.Controllers;

/// <summary>
/// Đăng nhập Google qua trình duyệt nhúng (Chrome Custom Tab / SFSafariViewController).
///
/// Khác <c>POST /auth/google</c> ở một điểm quyết định: ở đây Google chỉ nói chuyện với
/// BACKEND, nên app không cần đăng ký package name + SHA-1 với Google. Đổi lại backend phải tự
/// chạy luồng OAuth và giữ client secret.
///
/// Phần tra/tạo người dùng và phát JWT KHÔNG viết lại — nó đi qua đúng
/// <see cref="IGoogleLoginCommandHandler"/> mà đường native đang dùng.
/// </summary>
[ApiController]
[Route("api/v1/auth/google")]
[AllowAnonymous]
public class GoogleOAuthController : ControllerBase
{
    private readonly GoogleOAuthOptions _options;
    private readonly IGoogleOAuthStore _store;
    private readonly IGoogleCodeExchanger _codeExchanger;
    private readonly IGoogleLoginCommandHandler _loginHandler;

    public GoogleOAuthController(
        GoogleOAuthOptions options,
        IGoogleOAuthStore store,
        IGoogleCodeExchanger codeExchanger,
        IGoogleLoginCommandHandler loginHandler)
    {
        _options = options;
        _store = store;
        _codeExchanger = codeExchanger;
        _loginHandler = loginHandler;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new BusinessRuleException(
                AuthErrorCodes.GoogleOAuthNotConfigured,
                "Đăng nhập Google qua trình duyệt chưa được cấu hình trên máy chủ.");
        }
    }

    /// <summary>
    /// Mở đầu: đẩy trình duyệt sang Google. Trả 302 và HTML chứ không trả <c>ApiResponse</c> —
    /// đây là điều hướng trình duyệt, mà trình duyệt không đọc JSON bao bọc.
    /// </summary>
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpGet("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        EnsureEnabled();

        var state = await _store.IssueStateAsync(ct);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _options.ClientId;
        query["redirect_uri"] = _options.RedirectUri;
        query["response_type"] = "code";
        query["scope"] = "openid email profile";
        query["state"] = state;

        // Bắt Google hiện màn chọn tài khoản mỗi lần, thay vì im lặng dùng lại phiên cũ —
        // trên máy dùng chung thì im lặng đăng nhập lại người trước là một bất ngờ khó chịu.
        query["prompt"] = "select_account";

        return Redirect($"https://accounts.google.com/o/oauth2/v2/auth?{query}");
    }

    /// <summary>
    /// Google gọi về đây. Đổi code lấy id_token, phát JWT, rồi trả người dùng về app kèm một
    /// MÃ BÀN GIAO — không phải kèm token.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
        CancellationToken ct)
    {
        EnsureEnabled();

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            // Người dùng bấm Huỷ ở màn Google, hoặc Google từ chối.
            return Fail("Đăng nhập Google đã bị huỷ hoặc không thành công.");
        }

        if (!await _store.ConsumeStateAsync(state ?? "", ct))
        {
            return Fail("Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.");
        }

        var idToken = await _codeExchanger.ExchangeForIdTokenAsync(code, ct);
        if (idToken is null)
        {
            return Fail("Không xác thực được với Google. Vui lòng thử lại.");
        }

        var result = await _loginHandler.HandleAsync(
            new GoogleLoginCommand(idToken, HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        var handoff = await _store.IssueHandoffAsync(result, ct);

        if (string.IsNullOrWhiteSpace(_options.MobileDeepLink))
        {
            return HandoffPage(handoff);
        }

        var separator = _options.MobileDeepLink.Contains('?') ? "&" : "?";
        return Redirect($"{_options.MobileDeepLink}{separator}code={Uri.EscapeDataString(handoff)}");
    }

    /// <summary>
    /// App đổi mã bàn giao lấy JWT. Đây mới là API thật nên giữ đúng khuôn <c>ApiResponse</c>.
    /// </summary>
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange(
        [FromBody] GoogleHandoffRequest request, CancellationToken ct)
    {
        EnsureEnabled();

        var result = await _store.ConsumeHandoffAsync(request.Code, ct)
            ?? throw new AuthenticationException(
                AuthErrorCodes.TokenInvalid, "Mã đăng nhập không hợp lệ hoặc đã hết hạn.");

        return Ok(ApiResponse<AuthResultDto>.Ok(result));
    }

    /// <summary>
    /// Chế độ thử khi chưa có app: hiện mã ra để copy. Trang tối giản có chủ ý — nó chỉ tồn
    /// tại để kiểm chứng backend, không phải giao diện cho người dùng thật.
    /// </summary>
    private ContentResult HandoffPage(string handoff) => Content(
        $"""
        <!doctype html><meta charset="utf-8">
        <title>FinMate — mã đăng nhập</title>
        <body style="font-family:system-ui;padding:2rem">
        <h2>Đăng nhập Google thành công</h2>
        <p>Mã bàn giao (dùng một lần, hết hạn sau 2 phút):</p>
        <pre style="font-size:1.2rem;background:#f4f4f5;padding:1rem;border-radius:8px">{handoff}</pre>
        <p>Đổi lấy token: <code>POST /api/v1/auth/google/exchange</code> với body
        <code>code</code> bằng chuỗi trên.</p>
        </body>
        """,
        "text/html");

    private ContentResult Fail(string message) => Content(
        $"""
        <!doctype html><meta charset="utf-8">
        <title>FinMate — đăng nhập thất bại</title>
        <body style="font-family:system-ui;padding:2rem"><h2>{message}</h2></body>
        """,
        "text/html");
}

public record GoogleHandoffRequest(string Code);
