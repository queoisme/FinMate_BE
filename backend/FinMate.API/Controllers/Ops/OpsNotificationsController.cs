using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>Chuông thông báo của web vận hành. Mọi truy vấn lọc theo người gọi lấy từ token; người ngoài đội vận hành nhận 403.</summary>
[Route("api/v1/ops/notifications")]
public class OpsNotificationsController : OpsControllerBase
{
    private readonly IOpsNotificationService _notifications;

    public OpsNotificationsController(IOpsNotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false, [FromQuery] string? search = null, [FromQuery] int offset = 0, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var page = await _notifications.ListAsync(CurrentActorId, unreadOnly, search, offset, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<OpsNotificationDto>>.Ok(page.Items, page.ToMeta()));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Ok(ApiResponse<OpsUnreadCountDto>.Ok(new OpsUnreadCountDto(await _notifications.CountUnreadAsync(CurrentActorId, ct))));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(CurrentActorId, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
        => Ok(ApiResponse<OpsMarkedReadDto>.Ok(new OpsMarkedReadDto(await _notifications.MarkAllReadAsync(CurrentActorId, ct))));
}

public record OpsUnreadCountDto(int Unread);

public record OpsMarkedReadDto(int Marked);
