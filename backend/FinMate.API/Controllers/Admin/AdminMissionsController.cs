using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/missions")]
public class AdminMissionsController : AdminControllerBase
{
    private readonly IGetMissionListQueryHandler _listHandler;
    private readonly ICreateMissionCommandHandler _createHandler;
    private readonly IUpdateMissionCommandHandler _updateHandler;
    private readonly ISetMissionActivationCommandHandler _activationHandler;

    public AdminMissionsController(
        IGetMissionListQueryHandler listHandler,
        ICreateMissionCommandHandler createHandler,
        IUpdateMissionCommandHandler updateHandler,
        ISetMissionActivationCommandHandler activationHandler)
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _activationHandler = activationHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var missions = await _listHandler.HandleAsync(new GetMissionListQuery(includeInactive), ct);
        return Ok(ApiResponse<IReadOnlyList<AdminMissionDto>>.Ok(missions));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMissionRequest request, CancellationToken ct)
    {
        var mission = await _createHandler.HandleAsync(
            new CreateMissionCommand(
                CurrentAdminId, request.Code, request.Title, request.Description,
                request.PeriodType, request.ConditionType, request.ConditionTarget,
                request.ExpReward, CurrentIpAddress),
            ct);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<AdminMissionDto>.Ok(mission));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateMissionRequest request, CancellationToken ct)
    {
        var mission = await _updateHandler.HandleAsync(
            new UpdateMissionCommand(
                CurrentAdminId, id, request.Code, request.Title, request.Description,
                request.PeriodType, request.ConditionType, request.ConditionTarget,
                request.ExpReward, CurrentIpAddress),
            ct);

        return Ok(ApiResponse<AdminMissionDto>.Ok(mission));
    }

    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(
        Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
    {
        var mission = await _activationHandler.HandleAsync(
            new SetMissionActivationCommand(CurrentAdminId, id, request.IsActive, CurrentIpAddress), ct);

        return Ok(ApiResponse<AdminMissionDto>.Ok(mission));
    }
}

public record CreateMissionRequest(
    string Code,
    string Title,
    string Description,
    MissionPeriodType PeriodType,
    MissionConditionType ConditionType,
    int ConditionTarget,
    int ExpReward);

public record UpdateMissionRequest(
    string? Code,
    string? Title,
    string? Description,
    MissionPeriodType? PeriodType,
    MissionConditionType? ConditionType,
    int? ConditionTarget,
    int? ExpReward);
