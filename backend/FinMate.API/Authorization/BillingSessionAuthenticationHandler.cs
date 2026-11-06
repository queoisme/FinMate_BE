using System.Security.Claims;
using System.Text.Encodings.Web;
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
}
