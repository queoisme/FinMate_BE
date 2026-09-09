using System.Security.Claims;
using FinMate.Application.Auth.Commands;
using FinMate.Application.Auth.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IGetUserProfileQueryHandler _getProfileHandler;
    private readonly IUpdateUserProfileCommandHandler _updateProfileHandler;
    private readonly IUpdateNotificationPrefsCommandHandler _updateNotificationPrefsHandler;

    public UsersController(
        IGetUserProfileQueryHandler getProfileHandler,
        IUpdateUserProfileCommandHandler updateProfileHandler,
        IUpdateNotificationPrefsCommandHandler updateNotificationPrefsHandler)
    {
        _getProfileHandler = getProfileHandler;
        _updateProfileHandler = updateProfileHandler;
        _updateNotificationPrefsHandler = updateNotificationPrefsHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await _getProfileHandler.HandleAsync(new GetUserProfileQuery(CurrentUserId), ct);
        return Ok(ApiResponse<UserProfileDto>.Ok(profile));
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var profile = await _updateProfileHandler.HandleAsync(
            new UpdateUserProfileCommand(CurrentUserId, request.DisplayName), ct);
        return Ok(ApiResponse<UserProfileDto>.Ok(profile));
    }

    [HttpPatch("me/notification-prefs")]
    public async Task<IActionResult> UpdateNotificationPrefs(
        [FromBody] UpdateNotificationPrefsRequest request, CancellationToken ct)
    {
        await _updateNotificationPrefsHandler.HandleAsync(
            new UpdateNotificationPrefsCommand(
                CurrentUserId,
                request.PushEnabled,
                request.BudgetAlertsEnabled,
                request.MissionRemindersEnabled),
            ct);
        return NoContent();
    }
}

public record UpdateProfileRequest(string DisplayName);
public record UpdateNotificationPrefsRequest(bool PushEnabled, bool BudgetAlertsEnabled, bool MissionRemindersEnabled);
