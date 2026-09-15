using System.Security.Claims;
using FinMate.Application.Auth.Commands;
using FinMate.API.Middleware;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegisterCommandHandler _registerHandler;
    private readonly ILoginCommandHandler _loginHandler;
    private readonly IGoogleLoginCommandHandler _googleLoginHandler;
    private readonly IRefreshTokenCommandHandler _refreshHandler;
    private readonly ISendEmailVerificationCommandHandler _sendVerificationHandler;
    private readonly IVerifyEmailCommandHandler _verifyEmailHandler;
    private readonly IForgotPasswordCommandHandler _forgotPasswordHandler;
    private readonly IResetPasswordCommandHandler _resetPasswordHandler;
    private readonly ILogoutCommandHandler _logoutHandler;
    private readonly ILogoutAllDevicesCommandHandler _logoutAllHandler;
    private readonly IChangePasswordCommandHandler _changePasswordHandler;
    private readonly IDeleteAccountCommandHandler _deleteAccountHandler;

    public AuthController(
        IRegisterCommandHandler registerHandler,
        ILoginCommandHandler loginHandler,
        IGoogleLoginCommandHandler googleLoginHandler,
        IRefreshTokenCommandHandler refreshHandler,
        ISendEmailVerificationCommandHandler sendVerificationHandler,
        IVerifyEmailCommandHandler verifyEmailHandler,
        IForgotPasswordCommandHandler forgotPasswordHandler,
        IResetPasswordCommandHandler resetPasswordHandler,
        ILogoutCommandHandler logoutHandler,
        ILogoutAllDevicesCommandHandler logoutAllHandler,
        IChangePasswordCommandHandler changePasswordHandler,
        IDeleteAccountCommandHandler deleteAccountHandler)
    {
        _registerHandler = registerHandler;
        _loginHandler = loginHandler;
        _googleLoginHandler = googleLoginHandler;
        _refreshHandler = refreshHandler;
        _sendVerificationHandler = sendVerificationHandler;
        _verifyEmailHandler = verifyEmailHandler;
        _forgotPasswordHandler = forgotPasswordHandler;
        _resetPasswordHandler = resetPasswordHandler;
        _logoutHandler = logoutHandler;
        _logoutAllHandler = logoutAllHandler;
        _changePasswordHandler = changePasswordHandler;
        _deleteAccountHandler = deleteAccountHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Hạn mức chặt hơn mặc định: đây là các endpoint đoán được mật khẩu. Phân vùng theo IP vì
    /// chúng ẩn danh — chưa có user id để phân vùng theo.
    ///
    /// <c>refresh</c> cố ý KHÔNG nằm trong nhóm này: refresh token là chuỗi ngẫu nhiên nên dò
    /// không có ý nghĩa, mà siết nó sẽ chặn nhầm nhiều người dùng chung một NAT.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var profile = await _registerHandler.HandleAsync(
            new RegisterCommand(request.Email, request.Password, request.DisplayName), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<UserProfileDto>.Ok(profile));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _loginHandler.HandleAsync(
            new LoginCommand(request.Email, request.Password, RemoteIp), ct);
        return Ok(ApiResponse<AuthResultDto>.Ok(result));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken ct)
    {
        var result = await _googleLoginHandler.HandleAsync(
            new GoogleLoginCommand(request.IdToken, RemoteIp), ct);
        return Ok(ApiResponse<AuthResultDto>.Ok(result));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _refreshHandler.HandleAsync(
            new RefreshTokenCommand(request.RefreshToken, RemoteIp), ct);
        return Ok(ApiResponse<AuthResultDto>.Ok(result));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken ct)
    {
        await _logoutHandler.HandleAsync(new LogoutCommand(request.RefreshToken), ct);
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _logoutAllHandler.HandleAsync(new LogoutAllDevicesCommand(CurrentUserId), ct);
        return NoContent();
    }

    /// <summary>
    /// Gửi lại mã xác minh. Im lặng thành công khi email không tồn tại hoặc đã xác minh — nói
    /// khác đi là biến endpoint ẩn danh này thành máy dò tài khoản.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(
        [FromBody] EmailOnlyRequest request, CancellationToken ct)
    {
        await _sendVerificationHandler.HandleAsync(new SendEmailVerificationCommand(request.Email), ct);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        await _verifyEmailHandler.HandleAsync(new VerifyEmailCommand(request.Email, request.Code), ct);
        return NoContent();
    }

    /// <summary>
    /// LUÔN trả 204, kể cả khi email không tồn tại hoặc vừa xin mã cách đây vài giây. Khác đi
    /// là để lộ tài khoản nào có thật.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] EmailOnlyRequest request, CancellationToken ct)
    {
        await _forgotPasswordHandler.HandleAsync(new ForgotPasswordCommand(request.Email), ct);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _resetPasswordHandler.HandleAsync(
            new ResetPasswordCommand(request.Email, request.Code, request.NewPassword), ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await _changePasswordHandler.HandleAsync(
            new ChangePasswordCommand(CurrentUserId, request.CurrentPassword, request.NewPassword), ct);
        return NoContent();
    }

    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request, CancellationToken ct)
    {
        await _deleteAccountHandler.HandleAsync(new DeleteAccountCommand(CurrentUserId, request.Password), ct);
        return NoContent();
    }
}

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record GoogleLoginRequest(string IdToken);
public record RefreshRequest(string RefreshToken);
public record EmailOnlyRequest(string Email);
public record VerifyEmailRequest(string Email, string Code);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteAccountRequest(string Password);
