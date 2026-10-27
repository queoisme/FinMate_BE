using FinMate.Application.Admin.FinanceCatalog;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Quản trị mẫu mục tiêu tiết kiệm dùng để gợi ý. Không xóa — tắt bằng activation. Liên kết với phương pháp nằm ở <c>/admin/finance-methods/{id}/goal-templates</c>.</summary>
[Route("api/v1/admin/goal-templates")]
public class AdminGoalTemplatesController : AdminControllerBase
{
    private readonly IAdminFinanceCatalogService _catalog;

    public AdminGoalTemplatesController(IAdminFinanceCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AdminGoalTemplateDto>>.Ok(await _catalog.ListTemplatesAsync(includeInactive, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoalTemplateRequest request, CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<AdminGoalTemplateDto>.Ok(await _catalog.CreateTemplateAsync(CurrentAdminId, CurrentIpAddress, request, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGoalTemplateRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminGoalTemplateDto>.Ok(await _catalog.UpdateTemplateAsync(CurrentAdminId, CurrentIpAddress, id, request, ct)));

    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminGoalTemplateDto>.Ok(
            await _catalog.SetTemplateActiveAsync(CurrentAdminId, CurrentIpAddress, id, request.IsActive, ct)));
}
