using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Quản trị TÀI LIỆU CẤU HÌNH có phiên bản của AI/OCR cho màn hình admin trên web: từ vựng hiểu câu chat (<c>intent_cues</c>), từ điển phân loại chat
/// (<c>chat_dictionary</c>), từ điển tên cửa hàng (<c>merchant_lexicon</c>), từ vựng đọc hóa đơn (<c>parsing_vocab</c>), ngưỡng đọc hóa đơn (<c>thresholds</c>)
/// và chữ nhận dạng màn hình app (<c>app_screens</c>). Backend không đọc hay lưu nội dung: tài liệu nằm ở <c>config_documents</c> của AI Service. Controller này
/// chỉ giữ cổng (AdminOnly), chuyển tiếp NGUYÊN VĂN tới <c>/api/v1/admin/config</c> của AI Service, và ghi nhật ký admin cho mỗi lần lưu nháp / kích hoạt thành công.
///
/// Luồng sửa: <c>GET documents/{name}</c> (tải bản đang chạy, kèm <c>version</c>) → sửa → <c>POST documents/{name}/drafts</c> (nháp, được kiểm tra) →
/// <c>POST .../versions/{v}/preview</c> → <c>POST .../versions/{v}/activate</c> với <c>expected_active_version</c> = số phiên bản đã tải. Hoàn tác là kích hoạt lại một
/// phiên bản cũ. Thân và phản hồi giữ tên trường của AI Service (snake_case). Lỗi giữ mã HTTP của AI Service: 404 không có, 409 người khác vừa đổi
/// (<c>stale_active</c>) hoặc đã chạy rồi, 422 dữ liệu sai hoặc kích hoạt làm tụt độ chính xác (<c>preview_regression</c>, bỏ qua bằng <c>force</c>).
/// Hiệu lực: ai-service áp dụng ngay (<c>intent_cues</c>, <c>chat_dictionary</c>, <c>merchant_lexicon</c>); ocr-service tự nạp trong vòng khoảng 30 giây
/// (<c>parsing_vocab</c>, <c>thresholds</c>, <c>app_screens</c>).
/// </summary>
[Route("api/v1/admin/ai-config")]
public class AdminAiConfigController : AdminControllerBase
{
    private readonly IAdminAiConfigService _service;

    public AdminAiConfigController(IAdminAiConfigService service)
    {
        _service = service;
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private IReadOnlyDictionary<string, string?> QueryOf()
        => Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    private async Task<IActionResult> ReadAsync(string path, CancellationToken ct, HttpMethod? method = null)
    {
        var data = await _service.ReadAsync(method ?? HttpMethod.Get, path, QueryOf(), null, ct);
        return Ok(ApiResponse<JsonElement?>.Ok(data));
    }

    private async Task<IActionResult> WriteAsync(
        string path, string document, string action, int? version, JsonElement body, CancellationToken ct, bool created = false)
    {
        var data = await _service.WriteAsync(new AdminAiConfigWrite(CurrentAdminId, path, document, action, version, body, CurrentIpAddress), ct);
        var payload = ApiResponse<JsonElement?>.Ok(data);
        return created ? StatusCode(StatusCodes.Status201Created, payload) : Ok(payload);
    }

    // ---- đọc

    /// <summary>Các tài liệu sửa được: tên, mô tả, dịch vụ dùng, phiên bản đang chạy (0 = bản mặc định trong image), nguồn (<c>file</c>/<c>db</c>), số phiên bản đã lưu, có cổng đo khi xem trước không.</summary>
    [HttpGet("documents")]
    public Task<IActionResult> List(CancellationToken ct) => ReadAsync("documents", ct);

    /// <summary>Bản đang chạy (từ DB, hoặc bản mặc định nếu chưa ai sửa) kèm <c>version</c> để gửi lại ở <c>expected_active_version</c> khi kích hoạt.</summary>
    [HttpGet("documents/{name}")]
    public Task<IActionResult> GetActive(string name, CancellationToken ct) => ReadAsync($"documents/{Escape(name)}", ct);

    /// <summary>Bản mặc định trong image: để so sánh, hoặc đưa về nguyên trạng ban đầu (tải lên thành bản nháp rồi kích hoạt).</summary>
    [HttpGet("documents/{name}/default")]
    public Task<IActionResult> GetDefault(string name, CancellationToken ct) => ReadAsync($"documents/{Escape(name)}/default", ct);

    /// <summary>Lịch sử phiên bản (mới nhất trước): ai lưu, lúc nào, ghi chú, trạng thái draft/active/archived. Hỗ trợ <c>limit</c> và <c>offset</c>.</summary>
    [HttpGet("documents/{name}/versions")]
    public Task<IActionResult> ListVersions(string name, CancellationToken ct) => ReadAsync($"documents/{Escape(name)}/versions", ct);

    [HttpGet("documents/{name}/versions/{version:int}")]
    public Task<IActionResult> GetVersion(string name, int version, CancellationToken ct) => ReadAsync($"documents/{Escape(name)}/versions/{version}", ct);

    /// <summary>
    /// Khác biệt so với bản đang chạy, và (với tài liệu có cổng đo) kết quả chạy thử trên bộ câu có đáp án. Là POST vì chạy thử, nhưng không ghi gì
    /// và không để lại nhật ký. Tài liệu của ocr-service không xem trước được ở đây (<c>preview.available = false</c>): đo bằng benchmark của ocr-service sau khi kích hoạt.
    /// </summary>
    [HttpPost("documents/{name}/versions/{version:int}/preview")]
    public Task<IActionResult> Preview(string name, int version, CancellationToken ct)
        => ReadAsync($"documents/{Escape(name)}/versions/{version}/preview", ct, HttpMethod.Post);

    // ---- ghi

    /// <summary>
    /// Lưu bản nháp (chưa ảnh hưởng gì). Thân: <c>{"content": {...toàn bộ tài liệu sau khi sửa...}, "note": "vì sao đổi (tối thiểu 3 ký tự)"}</c>.
    /// Bị từ chối 422 nếu thiếu khóa mà code dùng, sai kiểu, chữ có dấu ở nơi cần bỏ dấu, regex không biên dịch được...
    /// </summary>
    [HttpPost("documents/{name}/drafts")]
    public Task<IActionResult> CreateDraft(string name, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync($"documents/{Escape(name)}/drafts", name, "draft", null, body, ct, created: true);

    /// <summary>
    /// Kích hoạt một phiên bản (bản nháp hoặc bản cũ để hoàn tác). Thân: <c>{"expected_active_version": N, "force": false}</c>. <c>expected_active_version</c> là phiên bản
    /// đang chạy mà người sửa nhìn thấy (0 = bản mặc định); khác thực tế thì bị từ chối 409 thay vì ghi đè thay đổi của người khác.
    /// </summary>
    [HttpPost("documents/{name}/versions/{version:int}/activate")]
    public Task<IActionResult> Activate(string name, int version, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync($"documents/{Escape(name)}/versions/{version}/activate", name, "activate", version, body, ct);
}
