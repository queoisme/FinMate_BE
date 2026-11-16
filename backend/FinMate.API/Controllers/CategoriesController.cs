using System.Security.Claims;
using FinMate.Application.Categories.Commands;
using FinMate.Application.Categories.Queries;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
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
    private readonly ISystemConfigProvider _config;

    public CategoriesController(
        ICreateCategoryCommandHandler createHandler,
        IUpdateCategoryCommandHandler updateHandler,
        IDeleteCategoryCommandHandler deleteHandler,
        IGetCategoryListQueryHandler listHandler,
        ISystemConfigProvider config)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _listHandler = listHandler;
        _config = config;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeHidden, CancellationToken ct)
    {
        var categories = await _listHandler.HandleAsync(new GetCategoryListQuery(CurrentUserId, includeHidden), ct);
        return Ok(ApiResponse<IReadOnlyList<CategoryDto>>.Ok(categories));
    }

    /// <summary>Bảng màu (cấu hình hệ thống, admin đổi được) và các loại danh mục cho màn thêm/sửa danh mục.</summary>
    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(CancellationToken ct)
    {
        var document = (await _config.GetAsync(ct)).Document;
        return Ok(ApiResponse<CategoryOptionsDto>.Ok(new CategoryOptionsDto(document.Categories.Palette, [Category.KindExpense, Category.KindIncome])));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await _createHandler.HandleAsync(
            new CreateCategoryCommand(
                CurrentUserId, request.Name, request.IconName, request.Color, request.Kind, request.ParentCategoryId, request.SuggestedBudgetCents, request.IsFavorite), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<CategoryDto>.Ok(category));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await _updateHandler.HandleAsync(
            new UpdateCategoryCommand(
                CurrentUserId, id, request.Name, request.IconName, request.Color, request.Kind, request.ParentCategoryId, request.SuggestedBudgetCents, request.IsFavorite), ct);
        return Ok(ApiResponse<CategoryDto>.Ok(category));
    }

    /// <summary>Ẩn/hiện danh mục hệ thống cho riêng người dùng này (không ảnh hưởng người khác, không đổi giao dịch cũ).</summary>
    [HttpPut("{id:guid}/hidden")]
    public async Task<IActionResult> SetHidden(Guid id, [FromBody] SetCategoryHiddenRequest request, [FromServices] ISetCategoryHiddenCommandHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new SetCategoryHiddenCommand(CurrentUserId, id, request.Hidden), ct);
        return Ok(ApiResponse<SetCategoryHiddenRequest>.Ok(request));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _deleteHandler.HandleAsync(new DeleteCategoryCommand(CurrentUserId, id), ct);
        return NoContent();
    }
}

/// <param name="Color"><c>#RRGGBB</c>.</param>
/// <param name="Kind"><c>expense</c> (mặc định) hoặc <c>income</c>.</param>
/// <param name="ParentCategoryId">Danh mục hệ thống làm nhóm cha.</param>
/// <param name="SuggestedBudgetCents">Hạn mức gợi ý mỗi tháng (đồng); chỉ để hiển thị.</param>
public record CreateCategoryRequest(
    string Name, string? IconName, string? Color = null, string? Kind = null, Guid? ParentCategoryId = null, long? SuggestedBudgetCents = null, bool IsFavorite = false);

/// <summary>Các trường mới: <c>null</c> = giữ nguyên; <c>parentCategoryId = 00000000-…</c> bỏ nhóm cha; <c>suggestedBudgetCents = 0</c> bỏ hạn mức.</summary>
public record UpdateCategoryRequest(
    string Name, string? IconName, string? Color = null, string? Kind = null, Guid? ParentCategoryId = null, long? SuggestedBudgetCents = null, bool? IsFavorite = null);

public record SetCategoryHiddenRequest(bool Hidden);
