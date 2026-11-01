using FinMate.Application.Admin.FinanceCatalog;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Quản trị các cách quản lý tài chính: nội dung, tham số, điều kiện chọn, vai trò, liên kết mẫu mục tiêu. Không xóa — tắt bằng activation.</summary>
[Route("api/v1/admin/finance-methods")]
public class AdminFinanceMethodsController : AdminControllerBase
{
    private readonly IAdminFinanceCatalogService _catalog;

    public AdminFinanceMethodsController(IAdminFinanceCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AdminFinanceMethodDto>>.Ok(await _catalog.ListMethodsAsync(includeInactive, ct)));

    /// <summary>Các loại khối giao diện trang chủ mà app biết vẽ (để chọn khi sửa <c>homeLayout</c>). Thêm loại mới cần code ở app.</summary>
    [HttpGet("home-blocks")]
    public IActionResult GetHomeBlockTypes()
        => Ok(ApiResponse<IReadOnlyList<HomeBlockTypeDto>>.Ok(FinanceHomeBlockCatalog.All));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFinanceMethodRequest request, CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<AdminFinanceMethodDto>.Ok(await _catalog.CreateMethodAsync(CurrentAdminId, CurrentIpAddress, request, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFinanceMethodRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminFinanceMethodDto>.Ok(await _catalog.UpdateMethodAsync(CurrentAdminId, CurrentIpAddress, id, request, ct)));

    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminFinanceMethodDto>.Ok(
            await _catalog.SetMethodActiveAsync(CurrentAdminId, CurrentIpAddress, id, request.IsActive, ct)));

    [HttpGet("{id:guid}/goal-templates")]
    public async Task<IActionResult> GetGoalTemplates(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AdminMethodTemplateLinkDto>>.Ok(await _catalog.GetMethodTemplatesAsync(id, ct)));

    /// <summary>Thay TOÀN BỘ liên kết mẫu mục tiêu của phương pháp bằng danh sách gửi lên.</summary>
    [HttpPut("{id:guid}/goal-templates")]
    public async Task<IActionResult> ReplaceGoalTemplates(Guid id, [FromBody] ReplaceGoalTemplatesRequest request, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AdminMethodTemplateLinkDto>>.Ok(
            await _catalog.ReplaceMethodTemplatesAsync(CurrentAdminId, CurrentIpAddress, id, request.Links ?? [], ct)));
}

public record ReplaceGoalTemplatesRequest(IReadOnlyList<MethodTemplateLinkRequest>? Links);
