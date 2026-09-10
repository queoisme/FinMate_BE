using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IAnalyzeNotificationCommandHandler _analyzeHandler;

    public NotificationsController(IAnalyzeNotificationCommandHandler analyzeHandler)
    {
        _analyzeHandler = analyzeHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeNotificationRequest request, CancellationToken ct)
    {
        var result = await _analyzeHandler.HandleAsync(
            new AnalyzeNotificationCommand(
                CurrentUserId,
                request.PackageName,
                request.NotificationTitle,
                request.NotificationBody,
                request.ReceivedAt),
            ct);
        return Ok(ApiResponse<NotificationAnalysisResultDto>.Ok(result));
    }
}

public record AnalyzeNotificationRequest(
    string PackageName,
    string? NotificationTitle,
    string NotificationBody,
    DateTimeOffset ReceivedAt);
