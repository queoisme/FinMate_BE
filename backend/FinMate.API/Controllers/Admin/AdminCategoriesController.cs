using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/categories")]
public class AdminCategoriesController : AdminControllerBase
{
    private readonly IGetSystemCategoryListQueryHandler _listHandler;
    private readonly ICreateSystemCategoryCommandHandler _createHandler;
    private readonly IUpdateSystemCategoryCommandHandler _updateHandler;
    private readonly ISetSystemCategoryActivationCommandHandler _activationHandler;

    public AdminCategoriesController(
        IGetSystemCategoryListQueryHandler listHandler,
        ICreateSystemCategoryCommandHandler createHandler,
        IUpdateSystemCategoryCommandHandler updateHandler,
        ISetSystemCategoryActivationCommandHandler activationHandler)
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _activationHandler = activationHandler;
    }

    /// <summary>Chỉ danh mục hệ thống. Danh mục riêng của người dùng không thuộc phạm vi admin.</summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var categories = await _listHandler.HandleAsync(new GetSystemCategoryListQuery(includeInactive), ct);
        return Ok(ApiResponse<IReadOnlyList<AdminCategoryDto>>.Ok(categories));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSystemCategoryRequest request, CancellationToken ct)
    {
        var category = await _createHandler.HandleAsync(
            new CreateSystemCategoryCommand(
                CurrentAdminId, request.Name, request.Slug, request.IconName, CurrentIpAddress),
            ct);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<AdminCategoryDto>.Ok(category));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSystemCategoryRequest request, CancellationToken ct)
    {
        var category = await _updateHandler.HandleAsync(
            new UpdateSystemCategoryCommand(
                CurrentAdminId, id, request.Name, request.Slug, request.IconName, CurrentIpAddress),
            ct);

        return Ok(ApiResponse<AdminCategoryDto>.Ok(category));
    }

    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(
        Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
    {
        var category = await _activationHandler.HandleAsync(
            new SetSystemCategoryActivationCommand(CurrentAdminId, id, request.IsActive, CurrentIpAddress), ct);

        return Ok(ApiResponse<AdminCategoryDto>.Ok(category));
    }
}

public record CreateSystemCategoryRequest(string Name, string Slug, string? IconName);
public record UpdateSystemCategoryRequest(string? Name, string? Slug, string? IconName);
