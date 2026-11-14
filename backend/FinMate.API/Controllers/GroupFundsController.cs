using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups.Funds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Quỹ chung của nhóm (Phase 85): sổ quỹ ảo thuộc về nhóm. Cần cả cờ <c>features.groups</c> và <c>features.groupFunds</c>.</summary>
[ApiController]
[Route("api/v1/groups/{groupId:guid}/funds")]
[Authorize]
[RequireGroupsEnabled]
[RequireGroupFundsEnabled]
public class GroupFundsController : ControllerBase
{
    private readonly IGroupFundService _funds;

    public GroupFundsController(IGroupFundService funds)
    {
        _funds = funds;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupFundDto>>.Ok(await _funds.ListAsync(groupId, CurrentUserId, ct)));

    [HttpGet("{fundId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid fundId, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.GetAsync(groupId, CurrentUserId, fundId, ct)));

    [HttpPost]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateGroupFundRequest request, CancellationToken ct)
    {
        var fund = await _funds.CreateAsync(new CreateGroupFundCommand(CurrentUserId, groupId, request.Name ?? string.Empty, request.Note), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupFundDto>.Ok(fund));
    }

    [HttpPatch("{fundId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid fundId, [FromBody] UpdateGroupFundRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.UpdateAsync(new UpdateGroupFundCommand(CurrentUserId, groupId, fundId, request.Name, request.Note), ct)));

    [HttpPost("{fundId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid groupId, Guid fundId, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.ArchiveAsync(groupId, CurrentUserId, fundId, ct)));
}

public record CreateGroupFundRequest(string? Name, string? Note);

public record UpdateGroupFundRequest(string? Name, string? Note);
