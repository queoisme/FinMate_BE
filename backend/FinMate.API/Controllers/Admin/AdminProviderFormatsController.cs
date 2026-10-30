using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Quản trị ĐỊNH DẠNG THÔNG BÁO theo nhà cung cấp (regex bóc số tiền, nội dung, số dư của từng ngân hàng/ví) cho màn hình admin trên web. Backend không đọc hay lưu nội dung:
/// định dạng nằm ở <c>kb_provider_formats</c> của AI Service. Controller này chỉ giữ cổng (AdminOnly), chuyển tiếp NGUYÊN VĂN tới <c>/api/v1/admin/provider-formats</c>
/// và ghi nhật ký admin cho mỗi lần ghi thành công (không kèm thân yêu cầu vì mẫu có thể chứa chữ thông báo).
///
/// Luồng sửa một regex mà không đụng bản đang chạy: <c>POST clone</c> (bản nháp từ bản mới nhất của package) → sửa pattern / trường / mẫu của bản nháp →
/// <c>POST {id}/try</c> (thử một thông báo, không ghi gì) → <c>GET {id}/check</c> → <c>POST {id}/activate</c> (chỉ thành công khi MỌI mẫu khớp; AI Service nạp lại trong khoảng 60 giây).
/// Sai thì <c>POST {id}/archive</c> bản mới, bản cũ vẫn chạy. Nhà cung cấp mới hoàn toàn: <c>POST</c> gốc tạo bản nháp trống. Thân và phản hồi giữ tên trường của AI Service
/// (snake_case). Lỗi giữ mã HTTP của AI Service: 404 không có, 409 không phải bản nháp, 422 dữ liệu sai.
/// </summary>
[Route("api/v1/admin/provider-formats")]
public class AdminProviderFormatsController : AdminControllerBase
{
    private readonly IAdminProviderFormatsService _service;

    public AdminProviderFormatsController(IAdminProviderFormatsService service)
    {
        _service = service;
    }

    private IReadOnlyDictionary<string, string?> QueryOf()
        => Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    private async Task<IActionResult> ReadAsync(string path, CancellationToken ct, HttpMethod? method = null, JsonElement? body = null)
    {
        var data = await _service.ReadAsync(method ?? HttpMethod.Get, path, QueryOf(), body, ct);
        return Ok(ApiResponse<JsonElement?>.Ok(data));
    }

    private async Task<IActionResult> WriteAsync(HttpMethod method, string path, string action, JsonElement? body, CancellationToken ct, bool created = false)
    {
        var data = await _service.WriteAsync(new AdminProviderFormatWrite(CurrentAdminId, method, path, action, body, CurrentIpAddress), ct);
        var payload = ApiResponse<JsonElement?>.Ok(data);
        return created ? StatusCode(StatusCodes.Status201Created, payload) : Ok(payload);
    }

    // ---- đọc

    /// <summary>Danh sách định dạng (mỗi package nhiều phiên bản): lọc <c>package_name</c>, <c>status</c> (draft/active/archived), <c>limit</c>, <c>offset</c>; kèm số pattern, trường, mẫu.</summary>
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => ReadAsync(string.Empty, ct);

    /// <summary>Một định dạng kèm toàn bộ pattern, trường và mẫu kiểm tra.</summary>
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => ReadAsync(id.ToString(), ct);

    /// <summary>Mọi lý do định dạng này CHƯA đủ điều kiện bật (rỗng = đủ). Không đổi dữ liệu.</summary>
    [HttpGet("{id:guid}/check")]
    public Task<IActionResult> Check(Guid id, CancellationToken ct) => ReadAsync($"{id}/check", ct);

    /// <summary>Thử một thông báo với các pattern đang bật của định dạng này. Là POST vì có thân, nhưng không ghi gì và không để lại nhật ký.</summary>
    [HttpPost("{id:guid}/try")]
    public Task<IActionResult> Try(Guid id, [FromBody] JsonElement body, CancellationToken ct) => ReadAsync($"{id}/try", ct, HttpMethod.Post, body);

    // ---- ghi: định dạng

    /// <summary>Bản nháp TRỐNG cho một nhà cung cấp. Đã có định dạng của package này thì dùng <c>clone</c>.</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, string.Empty, "create", body, ct, created: true);

    /// <summary>Bản nháp mới (phiên bản + 1) sao từ bản MỚI NHẤT của package, kèm pattern, trường và mẫu.</summary>
    [HttpPost("clone")]
    public Task<IActionResult> Clone([FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, "clone", "clone", body, ct, created: true);

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Patch, id.ToString(), "update", body, ct);

    /// <summary>Bản nháp thành bản đang chạy, chỉ khi MỌI mẫu khớp đúng kết quả mong đợi.</summary>
    [HttpPost("{id:guid}/activate")]
    public Task<IActionResult> Activate(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, $"{id}/activate", "activate", body, ct);

    /// <summary>Gỡ bản nháp hoặc bản đang chạy khỏi dùng; gỡ bản active cuối cùng của một package nghĩa là package đó quay về nhánh generic.</summary>
    [HttpPost("{id:guid}/archive")]
    public Task<IActionResult> Archive(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, $"{id}/archive", "archive", body, ct);

    // ---- ghi: pattern, trường, mẫu (chỉ trên bản nháp)

    [HttpPost("{id:guid}/patterns")]
    public Task<IActionResult> AddPattern(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, $"{id}/patterns", "pattern.add", body, ct, created: true);

    [HttpPatch("{id:guid}/patterns/{patternId:guid}")]
    public Task<IActionResult> UpdatePattern(Guid id, Guid patternId, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"{id}/patterns/{patternId}", "pattern.update", body, ct);

    [HttpDelete("{id:guid}/patterns/{patternId:guid}")]
    public Task<IActionResult> DeletePattern(Guid id, Guid patternId, CancellationToken ct) => WriteAsync(HttpMethod.Delete, $"{id}/patterns/{patternId}", "pattern.delete", null, ct);

    [HttpPost("{id:guid}/fields")]
    public Task<IActionResult> AddField(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, $"{id}/fields", "field.add", body, ct, created: true);

    [HttpPatch("{id:guid}/fields/{fieldId:guid}")]
    public Task<IActionResult> UpdateField(Guid id, Guid fieldId, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"{id}/fields/{fieldId}", "field.update", body, ct);

    [HttpDelete("{id:guid}/fields/{fieldId:guid}")]
    public Task<IActionResult> DeleteField(Guid id, Guid fieldId, CancellationToken ct) => WriteAsync(HttpMethod.Delete, $"{id}/fields/{fieldId}", "field.delete", null, ct);

    /// <summary>Mẫu kiểm tra: chữ thông báo ĐÃ che dữ liệu cá nhân kèm kết quả mong đợi. Người nhập chịu trách nhiệm đã che.</summary>
    [HttpPost("{id:guid}/samples")]
    public Task<IActionResult> AddSample(Guid id, [FromBody] JsonElement body, CancellationToken ct) => WriteAsync(HttpMethod.Post, $"{id}/samples", "sample.add", body, ct, created: true);

    [HttpDelete("{id:guid}/samples/{sampleId:guid}")]
    public Task<IActionResult> DeleteSample(Guid id, Guid sampleId, CancellationToken ct) => WriteAsync(HttpMethod.Delete, $"{id}/samples/{sampleId}", "sample.delete", null, ct);
}
