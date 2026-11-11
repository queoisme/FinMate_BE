using System.Text.Json;
using FinMate.API.Authorization;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Kho tri thức của AI Financial Agent (RAG) cho trang quản trị. Chuyển tiếp tới <c>/api/v1/admin/knowledge</c> của AI Service. Quy trình: gửi tài liệu (luôn <b>Draft</b>, phải có <c>source_name</c> và <c>as_of</c>)
/// → duyệt (<b>Approved</b>, từ lúc này agent mới dùng được) → gỡ (<b>Retired</b>). Mỗi lần gửi/duyệt/gỡ ghi một dòng audit_logs. Danh sách ứng viên là các câu hỏi agent chưa trả lời được: chỉ có
/// mã băm và số lần gặp, KHÔNG có nội dung và không bao giờ tự vào kho. Lỗi từ AI Service giữ nguyên: 404 không có, 409 chuyển trạng thái sai (<c>kb_invalid_transition</c>), 422 dữ liệu sai, 503 chưa nhúng được vector.
/// </summary>
[Route("api/v1/admin/knowledge")]
public class AdminKnowledgeController(IAdminKnowledgeService service) : AgentContentControllerBase
{
    private static Dictionary<string, string?> Query(params (string Name, object? Value)[] values)
        => values.Where(v => v.Value is not null).ToDictionary(v => v.Name, v => (string?)Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Phân trang và tìm kiếm trên máy chủ: <c>status</c> (Draft|Approved|Retired), <c>search</c> (theo tiêu đề / nguồn), <c>limit</c> (1–100), <c>offset</c>.</summary>
    [HttpGet("documents")]
    public async Task<IActionResult> ListDocuments([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.ListDocumentsAsync(Query(("status", status), ("search", search), ("limit", limit), ("offset", offset)), ct)));

    /// <summary>Một tài liệu kèm toàn bộ nội dung (<c>text</c>) để đọc trước khi duyệt. 404 nếu không có.</summary>
    [HttpGet("documents/{id:guid}")]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.GetDocumentAsync(id, ct)));

    /// <summary>Gửi tài liệu mới (Draft): <c>{title, source_name, source_url?, as_of, effective_from, effective_to?, text}</c>. Nội dung được cắt đoạn và nhúng vector.</summary>
    [HttpPost("documents")]
    public async Task<IActionResult> Submit([FromBody] JsonElement body, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<JsonElement?>.Ok(await service.SubmitAsync(CurrentAdminId, body, CurrentIpAddress, ct)));

    [HttpPost("documents/{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.ApproveAsync(CurrentAdminId, id, CurrentIpAddress, ct)));

    [HttpPost("documents/{id:guid}/retire")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.RetireAsync(CurrentAdminId, id, CurrentIpAddress, ct)));

    /// <summary>Câu hỏi chưa có tri thức, nhiều lần gặp nhất trước: <c>question_hash</c>, <c>count</c>, <c>first_seen</c>, <c>last_seen</c>, <c>status</c> (New|Dismissed|Covered).</summary>
    [HttpGet("candidates")]
    public async Task<IActionResult> ListCandidates([FromQuery] string? status, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.ListCandidatesAsync(Query(("status", status), ("limit", limit), ("offset", offset)), ct)));

    /// <summary>Đánh dấu đã xử lý: <c>{"status": "Dismissed" | "Covered"}</c>. Không tạo tài liệu.</summary>
    [HttpPost("candidates/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveCandidate(Guid id, [FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.ResolveCandidateAsync(CurrentAdminId, id, body, ct)));
}
