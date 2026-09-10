using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.SavingGoals.Queries;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/saving-goals")]
[Authorize]
public class SavingGoalsController : ControllerBase
{
    private readonly ICreateSavingGoalCommandHandler _createHandler;
    private readonly IUpdateSavingGoalCommandHandler _updateHandler;
    private readonly IContributeToGoalCommandHandler _contributeHandler;
    private readonly ICancelSavingGoalCommandHandler _cancelHandler;
    private readonly IGetSavingGoalListQueryHandler _listHandler;
    private readonly IGetGoalProgressQueryHandler _progressHandler;

    public SavingGoalsController(
        ICreateSavingGoalCommandHandler createHandler,
        IUpdateSavingGoalCommandHandler updateHandler,
        IContributeToGoalCommandHandler contributeHandler,
        ICancelSavingGoalCommandHandler cancelHandler,
        IGetSavingGoalListQueryHandler listHandler,
        IGetGoalProgressQueryHandler progressHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _contributeHandler = contributeHandler;
        _cancelHandler = cancelHandler;
        _listHandler = listHandler;
        _progressHandler = progressHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] SavingGoalStatus? status, CancellationToken ct)
    {
        var goals = await _listHandler.HandleAsync(new GetSavingGoalListQuery(CurrentUserId, status), ct);
        return Ok(ApiResponse<IReadOnlyList<SavingGoalDto>>.Ok(goals));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavingGoalRequest request, CancellationToken ct)
    {
        var goal = await _createHandler.HandleAsync(
            new CreateSavingGoalCommand(CurrentUserId, request.Name, request.TargetCents, request.Deadline), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<SavingGoalDto>.Ok(goal));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSavingGoalRequest request, CancellationToken ct)
    {
        var goal = await _updateHandler.HandleAsync(
            new UpdateSavingGoalCommand(CurrentUserId, id, request.Name, request.TargetCents, request.Deadline), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }

    [HttpGet("{id:guid}/progress")]
    public async Task<IActionResult> GetProgress(Guid id, CancellationToken ct)
    {
        var progress = await _progressHandler.HandleAsync(new GetGoalProgressQuery(CurrentUserId, id), ct);
        return Ok(ApiResponse<GoalProgressDto>.Ok(progress));
    }

    [HttpPost("{id:guid}/contribute")]
    public async Task<IActionResult> Contribute(Guid id, [FromBody] ContributeToGoalRequest request, CancellationToken ct)
    {
        var goal = await _contributeHandler.HandleAsync(
            new ContributeToGoalCommand(CurrentUserId, id, request.AmountCents, request.Note, request.ContributedAt), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var goal = await _cancelHandler.HandleAsync(new CancelSavingGoalCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<SavingGoalDto>.Ok(goal));
    }
}

public record CreateSavingGoalRequest(string Name, long TargetCents, DateTimeOffset? Deadline);
public record UpdateSavingGoalRequest(string Name, long TargetCents, DateTimeOffset? Deadline);
public record ContributeToGoalRequest(long AmountCents, string? Note, DateTimeOffset? ContributedAt);
