using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.API.Middleware;
using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Controllers.Ops;

/// <summary>Bước thứ hai của đăng nhập nội bộ. Không kế thừa <see cref="OpsControllerBase"/> vì người gọi chưa có claim đã xác thực.</summary>
[ApiController]
[Route("api/v1/ops/auth")]
[Authorize(Policy = AuthorizationPolicies.InternalAccount)]
public class OpsAuthController : ControllerBase
{
    private readonly IOpsTwoFactorService _twoFactor;
    private readonly OpsAuthOptions _options;

    public OpsAuthController(IOpsTwoFactorService twoFactor, OpsAuthOptions options)
    {
        _twoFactor = twoFactor;
        _options = options;
    }

    private Guid CurrentActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("status")]
    public IActionResult Status()
        => Ok(ApiResponse<OpsTwoFactorStatusDto>.Ok(new OpsTwoFactorStatusDto(
            _options.RequireTwoFactor,
            User.HasClaim(OpsClaims.Verified, OpsClaims.VerifiedValue))));

    [HttpPost("otp/send")]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    public async Task<IActionResult> SendCode(CancellationToken ct)
    {
        await _twoFactor.SendCodeAsync(CurrentActorId, ct);
        return NoContent();
    }

    /// <summary>Trả access token mới có claim đã xác thực; refresh token giữ nguyên.</summary>
    [HttpPost("otp/verify")]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    public async Task<IActionResult> Verify([FromBody] OpsOtpVerifyRequest request, CancellationToken ct)
        => Ok(ApiResponse<AuthResultDto>.Ok(await _twoFactor.VerifyAsync(
            CurrentActorId, request.Code, request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString(), ct)));
}

public record OpsOtpVerifyRequest(string? Code, string? RefreshToken);
