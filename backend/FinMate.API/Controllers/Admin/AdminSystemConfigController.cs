using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <param name="ExpectedVersion">Phiên bản đang chạy mà người sửa nhìn thấy (0 = chưa ai lưu). Khác thực tế thì bị từ chối 409 thay vì ghi đè thay đổi của người khác.</param>
/// <param name="Note">Lý do đổi, tối thiểu 3 ký tự.</param>
public sealed record SaveSystemConfigRequest(int ExpectedVersion, SystemConfigDocument Content, string Note);

public sealed record RollbackSystemConfigRequest(int ExpectedVersion, string Note);

/// <summary>
/// Quản trị CẤU HÌNH HỆ THỐNG không bí mật (chế độ bảo trì, phiên bản app, tính năng, ngưỡng, đồng bộ, giao diện). Mỗi lần lưu hoặc hoàn tác tạo một phiên bản mới
/// (lịch sử tuyến tính, không dòng nào bị ghi đè) và để lại một dòng audit_logs chỉ ghi tên các phần đã đổi. Key bên thứ ba KHÔNG nằm ở đây.
/// App chỉ đọc qua <c>GET /api/v1/app-config</c> (công khai, đọc một phần).
/// </summary>
[Route("api/v1/admin/system-config")]
public class AdminSystemConfigController : AdminControllerBase
{
    private const int MaxPageSize = 200;

    private readonly ISystemConfigService _service;

    public AdminSystemConfigController(ISystemConfigService service)
    {
        _service = service;
    }

    /// <summary>Bản đang chạy kèm <c>version</c> để gửi lại ở <c>expectedVersion</c> khi lưu. Version 0 là bản mặc định chưa ai lưu.</summary>
    [HttpGet]
    public async Task<IActionResult> GetActive(CancellationToken ct)
        => Ok(ApiResponse<SystemConfigView>.Ok(await _service.GetActiveAsync(ct)));

    /// <summary>Lịch sử phiên bản, mới nhất trước: ai lưu, lúc nào, lý do. Hỗ trợ <c>limit</c> và <c>offset</c>.</summary>
    [HttpGet("versions")]
    public async Task<IActionResult> ListVersions([FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken ct = default)
        => Ok(ApiResponse<SystemConfigVersionList>.Ok(await _service.ListVersionsAsync(Math.Clamp(limit, 1, MaxPageSize), Math.Max(offset, 0), ct)));

    [HttpGet("versions/{version:int}")]
    public async Task<IActionResult> GetVersion(int version, CancellationToken ct)
        => Ok(ApiResponse<SystemConfigView>.Ok(await _service.GetVersionAsync(version, ct)));

    /// <summary>Lưu nội dung mới thành phiên bản kế tiếp. 400 nếu dữ liệu sai, 409 nếu có người vừa lưu bản mới hơn, 422 nếu thiếu lý do.</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveSystemConfigRequest request, CancellationToken ct)
        => Ok(ApiResponse<SystemConfigView>.Ok(await _service.SaveAsync(CurrentAdminId, CurrentIpAddress, request.ExpectedVersion, request.Content, request.Note, ct)));

    /// <summary>Hoàn tác về một phiên bản cũ: tạo phiên bản MỚI có nội dung của bản đó.</summary>
    [HttpPost("rollback/{version:int}")]
    public async Task<IActionResult> Rollback(int version, [FromBody] RollbackSystemConfigRequest request, CancellationToken ct)
        => Ok(ApiResponse<SystemConfigView>.Ok(await _service.RollbackAsync(CurrentAdminId, CurrentIpAddress, version, request.ExpectedVersion, request.Note, ct)));
}
