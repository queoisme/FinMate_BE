using FinMate.API.Authorization;
using System.Security.Claims;
using FinMate.API.Middleware;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Controllers;

/// <summary>Người được mời xem và nhận lời mời. Mã nằm trong body, không trong URL: URL lọt vào log và lịch sử trình duyệt. Giới hạn tần suất chặt để chống dò mã.</summary>
[ApiController]
[Route("api/v1/group-invitations")]
[Authorize]
[EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
[RequireGroupsEnabled]
public class GroupInvitationsController : ControllerBase
{
    private readonly IGroupInvitationService _invitations;

    public GroupInvitationsController(IGroupInvitationService invitations)
    {
        _invitations = invitations;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] GroupInvitationTokenRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupInvitationPreviewDto>.Ok(await _invitations.PreviewAsync(CurrentUserId, request.Token ?? string.Empty, ct)));

    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] GroupInvitationTokenRequest request, CancellationToken ct)
        => Ok(ApiResponse<AcceptedGroupInvitationDto>.Ok(new AcceptedGroupInvitationDto(await _invitations.AcceptAsync(CurrentUserId, request.Token ?? string.Empty, ct))));
}

public record GroupInvitationTokenRequest(string? Token);
public record AcceptedGroupInvitationDto(Guid GroupId);
