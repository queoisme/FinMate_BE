using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Quản trị luật phân loại của AI (thương hiệu, từ khóa hóa đơn, bộ nhớ tên món, chỉ dẫn Gemini) cho màn hình admin trên web.
/// Backend không đọc hay lưu dữ liệu luật: luật nằm ở DB của AI Service. Controller này chỉ giữ cổng (AdminOnly), chuyển tiếp NGUYÊN VĂN tới
/// AI Service, và ghi nhật ký admin cho mỗi thay đổi thành công. Thân và phản hồi giữ tên trường của AI Service (snake_case, ví dụ
/// <c>category_slug</c>); xem <c>/meta</c> để biết các giá trị hợp lệ. Thay đổi có hiệu lực ngay, không cần khởi động lại.
/// </summary>
[Route("api/v1/admin/ai-rules")]
public class AdminAiRulesController : AdminControllerBase
{
    private readonly IAdminAiRulesService _service;

    public AdminAiRulesController(IAdminAiRulesService service)
    {
        _service = service;
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private IReadOnlyDictionary<string, string?> QueryOf()
        => Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    private static string? Prop(JsonElement? body, string name)
        => body is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private async Task<IActionResult> ReadAsync(string path, CancellationToken ct, HttpMethod? method = null, JsonElement? body = null)
    {
        var data = await _service.ReadAsync(method ?? HttpMethod.Get, path, QueryOf(), body, ct);
        return Ok(ApiResponse<JsonElement?>.Ok(data));
    }

    private async Task<IActionResult> WriteAsync(
        HttpMethod method, string path, string resource, string? key, JsonElement? body, CancellationToken ct,
        bool created = false, bool forwardQuery = false)
    {
        var data = await _service.WriteAsync(
            new AdminAiRuleWrite(CurrentAdminId, method, path, resource, key, forwardQuery ? QueryOf() : null, body, CurrentIpAddress), ct);
        if (data is null)
        {
            return NoContent();
        }

        var payload = ApiResponse<JsonElement?>.Ok(data);
        return created ? StatusCode(StatusCodes.Status201Created, payload) : Ok(payload);
    }

    // ---- đọc

    /// <summary>Giá trị hợp lệ để dựng ô chọn: nhóm của thương hiệu/từ khóa, nhóm của bộ nhớ tên món, loại từ khóa, giới hạn độ dài.</summary>
    [HttpGet("meta")]
    public Task<IActionResult> Meta(CancellationToken ct) => ReadAsync("meta", ct);

    [HttpGet("brands")]
    public Task<IActionResult> ListBrands(CancellationToken ct) => ReadAsync("brands", ct);

    [HttpGet("keywords")]
    public Task<IActionResult> ListKeywords(CancellationToken ct) => ReadAsync("keywords", ct);

    [HttpGet("item-cache")]
    public Task<IActionResult> ListItemCache(CancellationToken ct) => ReadAsync("item-cache", ct);

    [HttpGet("ai-instructions")]
    public Task<IActionResult> ListInstructions(CancellationToken ct) => ReadAsync("ai-instructions", ct);

    [HttpGet("history")]
    public Task<IActionResult> History(CancellationToken ct) => ReadAsync("history", ct);

    /// <summary>
    /// Thử trước khi lưu: luật hiện tại trả lời gì cho một cửa hàng / dòng chữ / tên món, và sẽ trả lời gì nếu thêm hoặc tắt các luật đề xuất.
    /// Là POST vì có thân, nhưng không ghi gì và không để lại nhật ký.
    /// </summary>
    [HttpPost("preview")]
    public Task<IActionResult> Preview([FromBody] JsonElement body, CancellationToken ct) => ReadAsync("preview", ct, HttpMethod.Post, body);

    // ---- ghi

    [HttpPut("brands/{brandKey}")]
    public Task<IActionResult> UpsertBrand(string brandKey, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Put, $"brands/{Escape(brandKey)}", "brands", brandKey, body, ct);

    [HttpPatch("brands/{brandKey}/active")]
    public Task<IActionResult> SetBrandActive(string brandKey, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"brands/{Escape(brandKey)}/active", "brands", brandKey, body, ct);

    [HttpPost("keywords")]
    public Task<IActionResult> CreateKeyword([FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, "keywords", "keywords", Prop(body, "keyword"), body, ct, created: true);

    [HttpPatch("keywords/{id:guid}/active")]
    public Task<IActionResult> SetKeywordActive(Guid id, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"keywords/{id}/active", "keywords", id.ToString(), body, ct);

    [HttpPut("item-cache")]
    public Task<IActionResult> PutItemCache([FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Put, "item-cache", "item-cache", Prop(body, "name"), body, ct);

    [HttpDelete("item-cache")]
    public Task<IActionResult> DeleteItemCache([FromQuery] string name, CancellationToken ct)
        => WriteAsync(HttpMethod.Delete, "item-cache", "item-cache", name, null, ct, forwardQuery: true);

    [HttpPost("ai-instructions")]
    public Task<IActionResult> CreateInstruction([FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, "ai-instructions", "ai-instructions", null, body, ct, created: true);

    [HttpPost("ai-instructions/{id:guid}/activate")]
    public Task<IActionResult> ActivateInstruction(Guid id, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"ai-instructions/{id}/activate", "ai-instructions", id.ToString(), null, ct);

    /// <summary>Đưa một dòng luật về trạng thái trước thay đổi của mục lịch sử này (thương hiệu/từ khóa vừa tạo thì bị tắt, không bị xóa).</summary>
    [HttpPost("history/{id:guid}/revert")]
    public Task<IActionResult> Revert(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, $"history/{id}/revert", "history", id.ToString(), body, ct);
}
