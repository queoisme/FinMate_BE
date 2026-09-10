using System.Security.Claims;
using FinMate.Application.Categories.Commands;
using FinMate.Application.Categories.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICreateCategoryCommandHandler _createHandler;
    private readonly IUpdateCategoryCommandHandler _updateHandler;
    private readonly IDeleteCategoryCommandHandler _deleteHandler;
    private readonly IGetCategoryListQueryHandler _listHandler;

    public CategoriesController(
        ICreateCategoryCommandHandler createHandler,
        IUpdateCategoryCommandHandler updateHandler,
        IDeleteCategoryCommandHandler deleteHandler,
        IGetCategoryListQueryHandler listHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _listHandler = listHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var categories = await _listHandler.HandleAsync(new GetCategoryListQuery(CurrentUserId), ct);
        return Ok(ApiResponse<IReadOnlyList<CategoryDto>>.Ok(categories));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await _createHandler.HandleAsync(
            new CreateCategoryCommand(CurrentUserId, request.Name, request.IconName), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<CategoryDto>.Ok(category));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await _updateHandler.HandleAsync(
            new UpdateCategoryCommand(CurrentUserId, id, request.Name, request.IconName), ct);
        return Ok(ApiResponse<CategoryDto>.Ok(category));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _deleteHandler.HandleAsync(new DeleteCategoryCommand(CurrentUserId, id), ct);
        return NoContent();
    }
}

public record CreateCategoryRequest(string Name, string? IconName);
public record UpdateCategoryRequest(string Name, string? IconName);
