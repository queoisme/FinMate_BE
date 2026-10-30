using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Quản trị KEY BÊN THỨ BA (Gemini, OpenRouter) cho màn hình admin trên web. Key nằm ở <c>secret_store</c> của AI Service, mã hóa AES-256-GCM bằng khóa chủ ở biến môi trường;
/// controller này chỉ giữ cổng (AdminOnly), chuyển tiếp tới <c>/api/v1/admin/secrets</c> và ghi nhật ký admin (tên key, hành động, lý do).
///
/// KHÔNG endpoint nào trả giá trị key, kể cả cho admin: chỉ trạng thái (nguồn <c>db</c>/<c>env</c>/<c>none</c>, 4 ký tự cuối, lúc đổi). Muốn đổi thì nhập key mới; muốn kiểm tra thì
/// <c>POST {name}/test</c>. Thân của <c>PUT</c> có giá trị key: không log ở đâu, không đưa vào nhật ký. Lỗi giữ mã HTTP của AI Service (404 không có key đó, 422 key không hợp lệ hoặc
/// chưa cấu hình khóa chủ). Key của backend (Brevo, Google, FCM) chưa nằm ở đây: chúng được dựng một lần lúc khởi động từ biến môi trường.
/// </summary>
[Route("api/v1/admin/secrets")]
public class AdminSecretsController : AdminControllerBase
{
    private readonly IAdminSecretsService _service;

    public AdminSecretsController(IAdminSecretsService service)
    {
        _service = service;
    }

    /// <summary>Từng key kèm trạng thái, và <c>available</c> = đã có khóa chủ để lưu key chưa.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ListAsync(ct)));

    /// <summary>Đặt hoặc ghi đè key. Thân: <c>{"value": "...", "reason": "..."}</c>. Trả trạng thái mới của key, không trả giá trị.</summary>
    [HttpPut("{name}")]
    public async Task<IActionResult> Set(string name, [FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.SetAsync(CurrentAdminId, CurrentIpAddress, name, body, ct)));

    /// <summary>Xóa key trong DB; AI Service quay về key ở biến môi trường nếu có.</summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, [FromQuery] string? reason, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.DeleteAsync(CurrentAdminId, CurrentIpAddress, name, reason, ct)));

    /// <summary>Thử key đang dùng bằng một lệnh gọi chỉ-đọc tới nhà cung cấp: <c>{"ok": bool, "detail": "..."}</c>. Là POST vì gọi ra ngoài, không ghi gì.</summary>
    [HttpPost("{name}/test")]
    public async Task<IActionResult> Test(string name, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.TestAsync(name, ct)));
}
