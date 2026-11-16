using System.Security.Claims;
using FinMate.Application.Devices.Commands;
using FinMate.Application.ListenerHealth;
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
    private readonly IReportListenerStatusCommandHandler _listenerStatusHandler;

    public DevicesController(
        IRegisterDeviceTokenCommandHandler registerHandler,
        IUnregisterDeviceTokenCommandHandler unregisterHandler,
        IReportListenerStatusCommandHandler listenerStatusHandler)
    {
        _registerHandler = registerHandler;
        _unregisterHandler = unregisterHandler;
        _listenerStatusHandler = listenerStatusHandler;
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

    /// <summary>
    /// BR-LH-01: app báo trạng thái dịch vụ đọc thông báo mỗi ngày một lần và mỗi khi quyền thay đổi. Thiết bị xác định bằng
    /// <c>installationId</c> do app sinh khi cài (D1), không bằng token FCM. Không nhận nội dung thông báo hay danh sách app khác.
    /// </summary>
    [HttpPut("listener-status")]
    public async Task<IActionResult> ReportListenerStatus([FromBody] ListenerStatusRequest request, CancellationToken ct)
    {
        await _listenerStatusHandler.HandleAsync(
            new ReportListenerStatusCommand(
                CurrentUserId, request.InstallationId, request.PermissionGranted, request.ListenerConnected,
                request.SelectedPackages, request.AppVersion, request.Manufacturer), ct);
        return NoContent();
    }
}

public record ListenerStatusRequest(
    Guid? InstallationId,
    bool? PermissionGranted,
    bool? ListenerConnected,
    List<ListenerPackageReport>? SelectedPackages,
    string? AppVersion,
    string? Manufacturer);

public record RegisterDeviceRequest(string Token, DevicePlatform Platform);
public record UnregisterDeviceRequest(string Token);
