using FinMate.API.Authorization;
using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Nhóm, thành viên và lời mời. Mọi kiểm tra quyền nằm ở <see cref="IGroupAccessService"/>; controller chỉ chuyển yêu cầu.</summary>
[ApiController]
[Route("api/v1/groups")]
[Authorize]
[RequireGroupsEnabled]
public class GroupsController : ControllerBase
{
    private const int DefaultActivityLimit = 50;

    private readonly IGroupService _groups;
    private readonly IGroupMemberService _members;
    private readonly IGroupInvitationService _invitations;

    public GroupsController(IGroupService groups, IGroupMemberService members, IGroupInvitationService invitations)
    {
        _groups = groups;
        _members = members;
        _invitations = invitations;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request, CancellationToken ct)
    {
        var created = await _groups.CreateAsync(new CreateGroupCommand(CurrentUserId, request.Name ?? string.Empty, request.Type ?? GroupType.Family), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupDto>.Ok(created));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupDto>>.Ok(await _groups.ListAsync(CurrentUserId, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<GroupDto>.Ok(await _groups.GetAsync(id, CurrentUserId, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGroupRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupDto>.Ok(await _groups.UpdateAsync(new UpdateGroupCommand(CurrentUserId, id, request.Name, request.TransactionVisibility), ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _groups.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/activities")]
    public async Task<IActionResult> Activities(Guid id, [FromQuery] int limit = DefaultActivityLimit, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<GroupActivityDto>>.Ok(await _groups.ListActivitiesAsync(id, CurrentUserId, limit, ct)));

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> Members(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupMemberDto>>.Ok(await _members.ListAsync(id, CurrentUserId, ct)));

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMember(Guid id, Guid userId, [FromBody] UpdateGroupMemberRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupMemberDto>.Ok(await _members.UpdateAsync(
            new UpdateGroupMemberCommand(CurrentUserId, id, userId, request.Role, request.GrantedPermissions, request.RevokedPermissions), ct)));

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        await _members.RemoveAsync(id, CurrentUserId, userId, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/alerts")]
    public async Task<IActionResult> SetAlerts(Guid id, [FromBody] SetGroupAlertsRequest request, CancellationToken ct)
    {
        await _members.SetAlertsAsync(id, CurrentUserId, request.Enabled, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        await _members.LeaveAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/transfer-ownership")]
    public async Task<IActionResult> TransferOwnership(Guid id, [FromBody] TransferGroupOwnershipRequest request, CancellationToken ct)
    {
        await _members.TransferOwnershipAsync(id, CurrentUserId, request.NewOwnerUserId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/invitations")]
    public async Task<IActionResult> CreateInvitation(Guid id, [FromBody] CreateGroupInvitationRequest request, CancellationToken ct)
    {
        var created = await _invitations.CreateAsync(new CreateGroupInvitationCommand(CurrentUserId, id, request.Email, request.Role ?? GroupRole.Member), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupInvitationDto>.Ok(created));
    }

    [HttpGet("{id:guid}/invitations")]
    public async Task<IActionResult> Invitations(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupInvitationDto>>.Ok(await _invitations.ListAsync(id, CurrentUserId, ct)));

    [HttpDelete("{id:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid id, Guid invitationId, CancellationToken ct)
    {
        await _invitations.RevokeAsync(id, CurrentUserId, invitationId, ct);
        return NoContent();
    }
}

public record CreateGroupRequest(string? Name, GroupType? Type);
public record UpdateGroupRequest(string? Name, GroupTransactionVisibility? TransactionVisibility);
public record UpdateGroupMemberRequest(GroupRole? Role, IReadOnlyList<GroupPermission>? GrantedPermissions, IReadOnlyList<GroupPermission>? RevokedPermissions);
public record TransferGroupOwnershipRequest(Guid NewOwnerUserId);
public record SetGroupAlertsRequest(bool Enabled);
public record CreateGroupInvitationRequest(string? Email, GroupRole? Role);
