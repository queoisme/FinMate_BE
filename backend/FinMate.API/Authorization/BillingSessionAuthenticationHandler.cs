using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FinMate.API.Authorization;

/// <summary>Xác thực phiên thanh toán của website. Scheme riêng, nên phiên này không mở được endpoint nào dùng JWT và JWT không mở được endpoint của website.</summary>
public sealed class BillingSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "BillingSession";
    public const string HeaderName = "X-Billing-Session";

    private readonly IBillingWebSessionStore _sessions;

    public BillingSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IBillingWebSessionStore sessions)
        : base(options, logger, encoder)
    {
        _sessions = sessions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var userId = await _sessions.GetSessionUserAsync(token, Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("Phiên thanh toán không hợp lệ.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    // Mặc định middleware xác thực trả body rỗng, khác envelope ApiResponse mà mọi lỗi khác trong app dùng
    // (xem JwtBearerEvents.OnChallenge trong Program.cs — cùng lý do, viết lại ở đây cho scheme riêng này).
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => WriteEnvelopeAsync(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Thiếu hoặc phiên thanh toán không hợp lệ.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => WriteEnvelopeAsync(StatusCodes.Status403Forbidden, "FORBIDDEN", "Bạn không có quyền truy cập tài nguyên này.");

    private async Task WriteEnvelopeAsync(int statusCode, string code, string message)
    {
        Response.ContentType = "application/json";
        Response.StatusCode = statusCode;
        var response = ApiResponse<object>.Fail(new ApiError(code, message));
        await Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }
}
