using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Gamification.Commands;
using FinMate.Application.Gamification.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/gamification")]
[Authorize]
public class GamificationController : ControllerBase
{
    private const int DefaultHistoryLimit = 20;

    private readonly IGetGamificationProfileQueryHandler _profileHandler;
    private readonly IGetActiveMissionsQueryHandler _missionsHandler;
    private readonly IGetMissionHistoryQueryHandler _historyHandler;
    private readonly IGetMascotInventoryQueryHandler _inventoryHandler;
    private readonly IUpdateMascotOutfitCommandHandler _outfitHandler;

    public GamificationController(
        IGetGamificationProfileQueryHandler profileHandler,
        IGetActiveMissionsQueryHandler missionsHandler,
        IGetMissionHistoryQueryHandler historyHandler,
        IGetMascotInventoryQueryHandler inventoryHandler,
        IUpdateMascotOutfitCommandHandler outfitHandler)
    {
        _profileHandler = profileHandler;
        _missionsHandler = missionsHandler;
        _historyHandler = historyHandler;
        _inventoryHandler = inventoryHandler;
        _outfitHandler = outfitHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var profile = await _profileHandler.HandleAsync(new GetGamificationProfileQuery(CurrentUserId), ct);
        return Ok(ApiResponse<GamificationProfileDto>.Ok(profile));
    }

    [HttpGet("missions")]
    public async Task<IActionResult> GetMissions(CancellationToken ct)
    {
        var missions = await _missionsHandler.HandleAsync(new GetActiveMissionsQuery(CurrentUserId), ct);
        return Ok(ApiResponse<IReadOnlyList<MissionDto>>.Ok(missions));
    }

    [HttpGet("missions/history")]
    public async Task<IActionResult> GetMissionHistory(CancellationToken ct)
    {
        var history = await _historyHandler.HandleAsync(
            new GetMissionHistoryQuery(CurrentUserId, DefaultHistoryLimit), ct);
        return Ok(ApiResponse<IReadOnlyList<MissionDto>>.Ok(history));
    }

    [HttpGet("mascot")]
    public async Task<IActionResult> GetMascot(CancellationToken ct)
    {
        var inventory = await _inventoryHandler.HandleAsync(new GetMascotInventoryQuery(CurrentUserId), ct);
        return Ok(ApiResponse<MascotInventoryDto>.Ok(inventory));
    }

    [HttpPut("mascot/outfit")]
    public async Task<IActionResult> UpdateOutfit([FromBody] UpdateMascotOutfitRequest request, CancellationToken ct)
    {
        var inventory = await _outfitHandler.HandleAsync(
            new UpdateMascotOutfitCommand(CurrentUserId, request.ItemIds ?? Array.Empty<Guid>()), ct);
        return Ok(ApiResponse<MascotInventoryDto>.Ok(inventory));
    }
}

/// <summary>Danh sách item muốn mặc; rỗng = cởi hết.</summary>
public record UpdateMascotOutfitRequest(IReadOnlyList<Guid>? ItemIds);
