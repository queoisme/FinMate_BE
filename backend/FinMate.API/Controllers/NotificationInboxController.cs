using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Hộp thư thông báo trong app (Phân hệ B, màn B2): "Tất cả", "Biến động", "Cảnh báo AI".</summary>
[ApiController]
[Route("api/v1/me/notifications")]
[Authorize]
public class NotificationInboxController : ControllerBase
{
    private readonly IUserNotificationService _notifications;

    public NotificationInboxController(IUserNotificationService notifications)
    {
        _notifications = notifications;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary><paramref name="tab"/>: <c>all</c> (mặc định), <c>movement</c> hoặc <c>ai_alert</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] string tab = "all", CancellationToken ct = default)
        => Ok(ApiResponse<UserNotificationListDto>.Ok(await _notifications.GetMineAsync(CurrentUserId, tab, ct)));

    /// <summary>Đánh dấu một thông báo là đã đọc.</summary>
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
        => Ok(ApiResponse<UserNotificationDto>.Ok(await _notifications.MarkReadAsync(CurrentUserId, id, ct)));

    /// <summary>Đánh dấu mọi thông báo là đã đọc.</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
        => Ok(ApiResponse<int>.Ok(await _notifications.MarkAllReadAsync(CurrentUserId, ct)));

    /// <summary>Xóa một thông báo.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _notifications.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>Xóa toàn bộ thông báo.</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        await _notifications.DeleteAllAsync(CurrentUserId, ct);
        return NoContent();
    }
}
