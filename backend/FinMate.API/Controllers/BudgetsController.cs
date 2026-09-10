using System.Security.Claims;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Budgets.Queries;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/budgets")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly ICreateBudgetCommandHandler _createHandler;
    private readonly IUpdateBudgetLimitCommandHandler _updateHandler;
    private readonly IDeleteBudgetCommandHandler _deleteHandler;
    private readonly IGetBudgetSummaryQueryHandler _summaryHandler;

    public BudgetsController(
        ICreateBudgetCommandHandler createHandler,
        IUpdateBudgetLimitCommandHandler updateHandler,
        IDeleteBudgetCommandHandler deleteHandler,
        IGetBudgetSummaryQueryHandler summaryHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _summaryHandler = summaryHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetSummary([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var summary = await _summaryHandler.HandleAsync(
            new GetBudgetSummaryQuery(CurrentUserId, year, month), ct);
        return Ok(ApiResponse<BudgetSummaryDto>.Ok(summary));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBudgetRequest request, CancellationToken ct)
    {
        var budget = await _createHandler.HandleAsync(
            new CreateBudgetCommand(CurrentUserId, request.CategoryId, request.LimitCents), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<BudgetDto>.Ok(budget));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateLimit(Guid id, [FromBody] UpdateBudgetLimitRequest request, CancellationToken ct)
    {
        var budget = await _updateHandler.HandleAsync(
            new UpdateBudgetLimitCommand(CurrentUserId, id, request.LimitCents), ct);
        return Ok(ApiResponse<BudgetDto>.Ok(budget));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _deleteHandler.HandleAsync(new DeleteBudgetCommand(CurrentUserId, id), ct);
        return NoContent();
    }
}

/// <summary>CategoryId null = hạn mức tổng cho toàn bộ chi tiêu.</summary>
public record CreateBudgetRequest(Guid? CategoryId, long LimitCents);
public record UpdateBudgetLimitRequest(long LimitCents);
