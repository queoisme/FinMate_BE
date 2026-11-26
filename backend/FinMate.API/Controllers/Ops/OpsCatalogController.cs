using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Catalog;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

public sealed record OpsCreateCategoryRequest(string Name, string Slug, string? IconName);

public sealed record OpsUpdateCategoryRequest(string? Name, string? Slug, string? IconName);

public sealed record OpsSetActivationRequest(bool IsActive);

/// <summary>
/// Phase 95 (B′): Staff duy trì danh mục hệ thống, thương hiệu và từ khoá phân loại, sửa trực tiếp (đăng ký: "maintain"), có audit <c>Ops.Catalog.*</c>.
/// Không có xoá, đổi slug, hoàn tác, bộ nhớ tên món, chỉ dẫn Gemini hay <c>ai-config</c> ở đây (vẫn chỉ Admin, ở <c>/admin/*</c>). Quyền <c>MaintainCatalog</c> kiểm ở service.
/// Thân và phản hồi của luật giữ tên trường của AI Service (snake_case), như <c>/admin/ai-rules</c>.
/// </summary>
[Route("api/v1/ops/catalog")]
public class OpsCatalogController : OpsControllerBase
{
    private const string HistoryResourceParam = "resource";
    private static readonly string[] HistoryResources = ["brand", "keyword"];

    private readonly IOpsCatalogService _catalog;

    public OpsCatalogController(IOpsCatalogService catalog)
    {
        _catalog = catalog;
    }

    // ---- danh mục hệ thống

    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories(
        [FromQuery] bool includeInactive, [FromQuery] string? search, [FromQuery] int offset, [FromQuery] int limit, CancellationToken ct)
    {
        var page = await _catalog.ListCategoriesAsync(CurrentActorId, new GetSystemCategoryListQuery(includeInactive, search, offset, limit), ct);
        return Ok(ApiResponse<IReadOnlyList<AdminCategoryDto>>.Ok(page.Items, page.ToMeta()));
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] OpsCreateCategoryRequest request, CancellationToken ct)
    {
        var category = await _catalog.CreateCategoryAsync(CurrentActorId, request.Name, request.Slug, request.IconName, CurrentIpAddress, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<AdminCategoryDto>.Ok(category));
    }

    /// <summary>Gửi <c>slug</c> khác slug hiện tại → 422 (slug bất biến).</summary>
    [HttpPatch("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] OpsUpdateCategoryRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminCategoryDto>.Ok(await _catalog.UpdateCategoryAsync(CurrentActorId, id, request.Name, request.Slug, request.IconName, CurrentIpAddress, ct)));

    /// <summary>Ẩn = tắt. Danh mục được bảo vệ (<c>income</c>, <c>loan</c>, <c>other</c>) → 422 <c>PROTECTED_CATEGORY</c>.</summary>
    [HttpPatch("categories/{id:guid}/activation")]
    public async Task<IActionResult> SetCategoryActivation(Guid id, [FromBody] OpsSetActivationRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminCategoryDto>.Ok(await _catalog.SetCategoryActivationAsync(CurrentActorId, id, request.IsActive, CurrentIpAddress, ct)));

    // ---- thương hiệu, từ khoá (luật ở AI Service)

    [HttpGet("ai-rules/meta")]
    public Task<IActionResult> Meta(CancellationToken ct) => ReadAsync("meta", ct);

    [HttpGet("ai-rules/brands")]
    public Task<IActionResult> ListBrands(CancellationToken ct) => ReadAsync("brands", ct);

    [HttpGet("ai-rules/keywords")]
    public Task<IActionResult> ListKeywords(CancellationToken ct) => ReadAsync("keywords", ct);

    /// <summary>Chỉ lịch sử của thương hiệu và từ khoá (D12): <c>?resource=brand|keyword</c> bắt buộc. Không có hoàn tác ở kênh này.</summary>
    [HttpGet("ai-rules/history")]
    public Task<IActionResult> History(CancellationToken ct)
    {
        var resource = Request.Query[HistoryResourceParam].ToString();
        if (!HistoryResources.Contains(resource, StringComparer.Ordinal))
        {
            throw new BadRequestException("CATALOG_HISTORY_RESOURCE", "Chỉ xem được lịch sử của brand hoặc keyword (tham số resource).");
        }

        return ReadAsync("history", ct);
    }

    /// <summary>Thử luật trước khi lưu; không ghi gì.</summary>
    [HttpPost("ai-rules/preview")]
    public async Task<IActionResult> Preview([FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _catalog.ReadRulesAsync(CurrentActorId, HttpMethod.Post, "preview", null, body, ct)));

    [HttpPut("ai-rules/brands/{brandKey}")]
    public Task<IActionResult> UpsertBrand(string brandKey, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Put, $"brands/{Escape(brandKey)}", "brands", brandKey, body, ct);

    [HttpPatch("ai-rules/brands/{brandKey}/active")]
    public Task<IActionResult> SetBrandActive(string brandKey, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"brands/{Escape(brandKey)}/active", "brands", brandKey, body, ct);

    [HttpPost("ai-rules/keywords")]
    public Task<IActionResult> AddKeyword([FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Post, "keywords", "keywords", KeywordOf(body), body, ct, created: true);

    [HttpPatch("ai-rules/keywords/{id:guid}/active")]
    public Task<IActionResult> SetKeywordActive(Guid id, [FromBody] JsonElement body, CancellationToken ct)
        => WriteAsync(HttpMethod.Patch, $"keywords/{id}/active", "keywords", id.ToString(), body, ct);

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static string? KeywordOf(JsonElement body)
        => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("keyword", out var keyword) && keyword.ValueKind == JsonValueKind.String ? keyword.GetString() : null;

    private async Task<IActionResult> ReadAsync(string path, CancellationToken ct)
    {
        var query = Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());
        return Ok(ApiResponse<JsonElement?>.Ok(await _catalog.ReadRulesAsync(CurrentActorId, HttpMethod.Get, path, query, null, ct)));
    }

    private async Task<IActionResult> WriteAsync(HttpMethod method, string path, string resource, string? key, JsonElement body, CancellationToken ct, bool created = false)
    {
        var data = await _catalog.WriteRuleAsync(CurrentActorId, method, path, resource, key, body, CurrentIpAddress, ct);
        var payload = ApiResponse<JsonElement?>.Ok(data);
        return created ? StatusCode(StatusCodes.Status201Created, payload) : Ok(payload);
    }
}
