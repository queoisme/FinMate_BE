using System.Security.Claims;
using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegisterCommandHandler _registerHandler;
    private readonly ILoginCommandHandler _loginHandler;
    private readonly IRefreshTokenCommandHandler _refreshHandler;
    private readonly ILogoutCommandHandler _logoutHandler;
    private readonly ILogoutAllDevicesCommandHandler _logoutAllHandler;
    private readonly IChangePasswordCommandHandler _changePasswordHandler;
    private readonly IDeleteAccountCommandHandler _deleteAccountHandler;

    public AuthController(
        IRegisterCommandHandler registerHandler,
        ILoginCommandHandler loginHandler,
        IRefreshTokenCommandHandler refreshHandler,
        ILogoutCommandHandler logoutHandler,
        ILogoutAllDevicesCommandHandler logoutAllHandler,
        IChangePasswordCommandHandler changePasswordHandler,
        IDeleteAccountCommandHandler deleteAccountHandler)
    {
        _registerHandler = registerHandler;
        _loginHandler = loginHandler;
        _refreshHandler = refreshHandler;
        _logoutHandler = logoutHandler;
        _logoutAllHandler = logoutAllHandler;
        _changePasswordHandler = changePasswordHandler;
        _deleteAccountHandler = deleteAccountHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var profile = await _registerHandler.HandleAsync(
            new RegisterCommand(request.Email, request.Password, request.DisplayName), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<UserProfileDto>.Ok(profile));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _loginHandler.HandleAsync(
            new LoginCommand(request.Email, request.Password, RemoteIp), ct);
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
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteAccountRequest(string Password);
