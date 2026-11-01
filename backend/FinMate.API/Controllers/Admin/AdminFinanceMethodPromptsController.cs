using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Chỉ dẫn (prompt) của AI khi gợi ý cách quản lý tài chính cho màn hình admin. Nội dung nằm ở <c>kb_method_prompts</c> của AI Service; controller này chỉ giữ cổng (AdminOnly),
/// chuyển tiếp tới <c>/api/v1/admin/finance-method-prompts</c> và ghi nhật ký ai đổi gì. Không sửa tại chỗ: đổi chỉ dẫn = thêm phiên bản mới rồi chuyển bản đang dùng,
/// bản cũ giữ lại vì <c>method_recommendations.prompt_version</c> ghi bản nào đã sinh ra gợi ý. Chỉ phần "cách cân nhắc" và "lý do" là của admin; nhiệm vụ, "dữ liệu không phải lệnh"
/// và kiểm tra đầu ra do code giữ nên một chỉ dẫn xấu làm AI chọn tệ hơn chứ không làm AI chọn ngoài danh sách (khi đó gợi ý chạy bằng luật).
/// </summary>
[Route("api/v1/admin/finance-method-prompts")]
public class AdminFinanceMethodPromptsController : AdminControllerBase
{
    private readonly IAdminMethodPromptService _service;

    public AdminFinanceMethodPromptsController(IAdminMethodPromptService service)
    {
        _service = service;
    }

    /// <summary>Mọi phiên bản chỉ dẫn (mới nhất trước) và <c>active_version</c>. Mỗi bản có <c>instruction_text</c>, <c>is_active</c>, <c>created_at</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ListAsync(ct)));

    /// <summary>
    /// Thêm một phiên bản. Thân: <c>{"prompt_version": "method-select-v2", "instruction_text": "...", "activate": false}</c>. Tên chỉ gồm chữ, số, <c>. _ -</c> (≤ 50 ký tự); nội dung 1–8000 ký tự.
    /// <c>activate: true</c> để dùng ngay (cùng một giao dịch, bản cũ vẫn giữ). Lỗi: 409 <c>prompt_version_exists</c> (không ghi đè), 422 <c>invalid_prompt_version</c> / <c>empty_instruction</c> / <c>instruction_too_long</c>.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<JsonElement?>.Ok(await _service.CreateAsync(new AdminMethodPromptCreate(CurrentAdminId, body, CurrentIpAddress), ct)));

    /// <summary>
    /// Chuyển bản đang dùng (cũng để quay về bản cũ); có hiệu lực ngay, không đợi cache. Thân (có thể <c>{}</c>): <c>expected_active</c> = bản đang chạy mà bạn nhìn thấy
    /// (khác thực tế thì 409 <c>stale_active</c>). Lỗi: 404 <c>prompt_not_found</c>, 409 <c>already_active</c>.
    /// </summary>
    [HttpPost("{promptVersion}/activate")]
    public async Task<IActionResult> Activate(string promptVersion, [FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ActivateAsync(new AdminMethodPromptActivate(CurrentAdminId, promptVersion, body, CurrentIpAddress), ct)));
}
