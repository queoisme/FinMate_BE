using System.Security.Claims;
using FinMate.Application.Devices.Commands;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Đăng ký/gỡ thiết bị nhận push. Client gọi <c>POST</c> mỗi lần mở app (FCM có thể cấp
/// token mới bất cứ lúc nào) và <c>DELETE</c> khi đăng xuất.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly IRegisterDeviceTokenCommandHandler _registerHandler;
    private readonly IUnregisterDeviceTokenCommandHandler _unregisterHandler;

    public DevicesController(
        IRegisterDeviceTokenCommandHandler registerHandler,
        IUnregisterDeviceTokenCommandHandler unregisterHandler)
    {
        _registerHandler = registerHandler;
        _unregisterHandler = unregisterHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        await _registerHandler.HandleAsync(
            new RegisterDeviceTokenCommand(CurrentUserId, request.Token, request.Platform), ct);
        return NoContent();
    }

    /// <summary>
    /// Token đi trong BODY chứ không phải path hay query, dù DELETE-có-body là hơi lạ.
    /// Token FCM là một capability — ai cầm được nó đều đẩy thông báo xuống máy đó — mà URL
    /// thì nằm trong access log, lịch sử proxy và báo cáo lỗi. Body không nằm ở những chỗ đó.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceRequest request, CancellationToken ct)
    {
        await _unregisterHandler.HandleAsync(
            new UnregisterDeviceTokenCommand(CurrentUserId, request.Token), ct);
        return NoContent();
    }
}

public record RegisterDeviceRequest(string Token, DevicePlatform Platform);
public record UnregisterDeviceRequest(string Token);
