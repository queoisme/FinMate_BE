using FinMate.API.Authorization;
using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Groups.Approvals;
using FinMate.Application.Groups.Bills;
using FinMate.Application.Groups.Invitations;
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
    private readonly IGroupService _groups;
    private readonly IGroupMemberService _members;
    private readonly IGroupInvitationService _invitations;
    private readonly IGroupSettingsService _settings;
    private readonly IGroupBillService _bills;
    private readonly IGroupDebtService _debts;
    private readonly IGroupSummaryService _summary;
    private readonly IGroupApprovalService _approvals;

    public GroupsController(IGroupService groups, IGroupMemberService members, IGroupInvitationService invitations, IGroupSettingsService settings,
        IGroupBillService bills, IGroupDebtService debts, IGroupSummaryService summary, IGroupApprovalService approvals)
    {
        _approvals = approvals;
        _bills = bills;
        _debts = debts;
        _summary = summary;
        _settings = settings;
        _groups = groups;
        _members = members;
        _invitations = invitations;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request, CancellationToken ct)
    {
        var created = await _groups.CreateAsync(new CreateGroupCommand(CurrentUserId, request.Name ?? string.Empty, request.Type ?? GroupType.Family, request.ToProfile()), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupDto>.Ok(created));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupDto>>.Ok(await _groups.ListAsync(CurrentUserId, ct)));

    [HttpGet("me/summary")]
    public async Task<IActionResult> MySummary([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        => Ok(ApiResponse<MyGroupSummaryDto>.Ok(await _summary.GetAsync(CurrentUserId, year, month, ct)));

    [HttpPost("{id:guid}/bills")]
    public async Task<IActionResult> CreateBill(Guid id, [FromBody] CreateBillRequest r, CancellationToken ct)
    {
        var command = new CreateGroupBillCommand(
            CurrentUserId, id, r.Title ?? string.Empty, r.AmountCents, r.PaidByUserId, r.OccurredAt ?? DateTimeOffset.UtcNow,
            r.SplitMode ?? GroupBillSplitMode.Equal, r.Splits ?? [], r.CategoryId, r.GroupCategoryId, r.Note);

        // Từ ngưỡng duyệt do người không phải Owner tạo: 202 và body là yêu cầu chờ duyệt, chưa có hóa đơn nào.
        if (command.AmountCents > 0 && await _approvals.TrySubmitBillAsync(command, ct) is { } pending)
        {
            return StatusCode(StatusCodes.Status202Accepted, ApiResponse<GroupApprovalDto>.Ok(pending));
        }

        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupBillDto>.Ok(await _bills.CreateAsync(command, ct)));
    }

    [HttpGet("{id:guid}/bills")]
    public async Task<IActionResult> Bills(Guid id, [FromQuery] string? cursor, [FromQuery] int limit, CancellationToken ct = default)
    {
        var result = await _bills.ListAsync(id, CurrentUserId, cursor, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<GroupBillDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

    [HttpGet("{id:guid}/bills/{billId:guid}")]
    public async Task<IActionResult> Bill(Guid id, Guid billId, CancellationToken ct)
        => Ok(ApiResponse<GroupBillDto>.Ok(await _bills.GetAsync(id, CurrentUserId, billId, ct)));

    [HttpGet("{id:guid}/approvals")]
    public async Task<IActionResult> Approvals(Guid id, [FromQuery] string? status, [FromQuery] int limit, CancellationToken ct = default)
    {
        return Ok(ApiResponse<IReadOnlyList<GroupApprovalDto>>.Ok(await _approvals.ListAsync(id, CurrentUserId, status, limit, ct)));
    }

    [HttpPost("{id:guid}/approvals/{approvalId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, Guid approvalId, CancellationToken ct)
        => Ok(ApiResponse<GroupApprovalDto>.Ok(await _approvals.ApproveAsync(id, CurrentUserId, approvalId, ct)));

    [HttpPost("{id:guid}/approvals/{approvalId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, Guid approvalId, [FromBody] RejectApprovalRequest? r, CancellationToken ct)
        => Ok(ApiResponse<GroupApprovalDto>.Ok(await _approvals.RejectAsync(id, CurrentUserId, approvalId, r?.Reason, ct)));

    [HttpGet("{id:guid}/balances")]
    public async Task<IActionResult> Balances(Guid id, CancellationToken ct)
        => Ok(ApiResponse<GroupBalancesDto>.Ok(await _debts.GetBalancesAsync(id, CurrentUserId, ct)));

    [HttpPost("{id:guid}/settlements")]
    public async Task<IActionResult> Settle(Guid id, [FromBody] CreateSettlementRequest r, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<GroupSettlementDto>.Ok(
            await _debts.SettleAsync(new CreateGroupSettlementCommand(CurrentUserId, id, r.FromUserId, r.ToUserId, r.AmountCents, r.Note), ct)));

    [HttpPost("{id:guid}/members/{userId:guid}/remind-debt")]
    public async Task<IActionResult> RemindDebt(Guid id, Guid userId, CancellationToken ct)
        => Ok(ApiResponse<GroupRemindResultDto>.Ok(await _debts.RemindAsync(id, CurrentUserId, userId, ct)));

    [HttpPost("{id:guid}/remind-all")]
    public async Task<IActionResult> RemindAll(Guid id, CancellationToken ct)
        => Ok(ApiResponse<GroupRemindResultDto>.Ok(await _debts.RemindAllAsync(id, CurrentUserId, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<GroupDto>.Ok(await _groups.GetAsync(id, CurrentUserId, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGroupRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupDto>.Ok(await _groups.UpdateAsync(new UpdateGroupCommand(CurrentUserId, id, request.Name, request.TransactionVisibility, request.ToProfile()), ct)));

    [HttpGet("{id:guid}/settings")]
    public async Task<IActionResult> GetSettings(Guid id, CancellationToken ct)
        => Ok(ApiResponse<GroupSettingsDto>.Ok(await _settings.GetAsync(id, CurrentUserId, ct)));

    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> PutSettings(Guid id, [FromBody] PutGroupSettingsRequest r, CancellationToken ct)
        => Ok(ApiResponse<GroupSettingsDto>.Ok(await _settings.UpdateAsync(new UpdateGroupSettingsCommand(
            CurrentUserId, id, r.ClosingDay, r.DebtSettlementMethod ?? DebtSettlementMethod.AiOptimized, r.RequireApprovalAboveCents,
            r.NotifyOnNewTransaction ?? true, r.WeeklySummary ?? false, r.WeeklySummaryDay ?? 1, r.WeeklySummaryTime), ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _groups.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/activities")]
    public async Task<IActionResult> Activities(Guid id, [FromQuery] string? cursor, [FromQuery] int limit, CancellationToken ct = default)
    {
        var result = await _groups.ListActivitiesAsync(id, CurrentUserId, cursor, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<GroupActivityDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

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
        var created = await _invitations.CreateAsync(new CreateGroupInvitationCommand(CurrentUserId, id, request.Email, request.Role ?? GroupRole.Member, request.Message), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupInvitationDto>.Ok(created));
    }

    /// <summary>Người nên mời: cùng nhóm khác với bạn, chưa ở nhóm này, chưa có lời mời chờ.</summary>
    [HttpGet("{id:guid}/invite-suggestions")]
    public async Task<IActionResult> InviteSuggestions(Guid id, [FromServices] IUserSearchService search, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupInviteSuggestionDto>>.Ok(await search.SuggestAsync(id, CurrentUserId, ct)));

    /// <summary>
    /// Mã/link tham gia chung của nhóm, luôn hiện sẵn (tạo luôn lần đầu mở): không hết hạn, dùng nhiều lần, vào với vai trò Member.
    /// Người vào dùng <c>POST /group-invitations/preview|accept</c> với <c>token = code</c>.
    /// </summary>
    [HttpGet("{id:guid}/join-link")]
    public async Task<IActionResult> JoinLink(Guid id, [FromServices] IGroupJoinLinkService joinLink, CancellationToken ct)
        => Ok(ApiResponse<GroupJoinLinkDto>.Ok(await joinLink.GetAsync(id, CurrentUserId, ct)));

    /// <summary>Đổi mã mới (mã cũ hết dùng được), dùng khi mã bị lộ.</summary>
    [HttpPost("{id:guid}/join-link/reset")]
    public async Task<IActionResult> ResetJoinLink(Guid id, [FromServices] IGroupJoinLinkService joinLink, CancellationToken ct)
        => Ok(ApiResponse<GroupJoinLinkDto>.Ok(await joinLink.ResetAsync(id, CurrentUserId, ct)));

    [HttpPut("{id:guid}/join-link")]
    public async Task<IActionResult> SetJoinLinkEnabled(Guid id, [FromBody] SetJoinLinkEnabledRequest request, [FromServices] IGroupJoinLinkService joinLink, CancellationToken ct)
        => Ok(ApiResponse<GroupJoinLinkDto>.Ok(await joinLink.SetEnabledAsync(id, CurrentUserId, request.Enabled, ct)));

    /// <summary>Thu hồi mọi lời mời đang chờ. Trả số lời mời đã thu hồi.</summary>
    [HttpDelete("{id:guid}/invitations")]
    public async Task<IActionResult> RevokeAllInvitations(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RevokedInvitationsDto>.Ok(new RevokedInvitationsDto(await _invitations.RevokeAllAsync(id, CurrentUserId, ct))));

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

public record CreateGroupRequest(
    string? Name, GroupType? Type, string? Icon = null, string? Description = null, string? Currency = null,
    GroupSettlementModel? SettlementModel = null, int? ClosingDay = null, DebtSettlementMethod? DebtSettlementMethod = null)
{
    public GroupProfile ToProfile() => new(Icon, Description, Currency, SettlementModel, ClosingDay, DebtSettlementMethod);
}
public record UpdateGroupRequest(
    string? Name, GroupTransactionVisibility? TransactionVisibility, string? Icon = null, string? Description = null, string? Currency = null,
    GroupSettlementModel? SettlementModel = null, int? ClosingDay = null, DebtSettlementMethod? DebtSettlementMethod = null)
{
    public GroupProfile ToProfile() => new(Icon, Description, Currency, SettlementModel, ClosingDay, DebtSettlementMethod);
}
public record UpdateGroupMemberRequest(GroupRole? Role, IReadOnlyList<GroupPermission>? GrantedPermissions, IReadOnlyList<GroupPermission>? RevokedPermissions);
public record TransferGroupOwnershipRequest(Guid NewOwnerUserId);
public record SetGroupAlertsRequest(bool Enabled);
public record CreateGroupInvitationRequest(string? Email, GroupRole? Role, string? Message = null);

public record PutGroupSettingsRequest(int? ClosingDay, DebtSettlementMethod? DebtSettlementMethod, long? RequireApprovalAboveCents, bool? NotifyOnNewTransaction, bool? WeeklySummary, int? WeeklySummaryDay, string? WeeklySummaryTime);
public record CreateBillRequest(
    string? Title, long AmountCents, Guid PaidByUserId, DateTimeOffset? OccurredAt, GroupBillSplitMode? SplitMode,
    IReadOnlyList<GroupBillSplitInput>? Splits, Guid? CategoryId, Guid? GroupCategoryId, string? Note);

public record CreateSettlementRequest(Guid FromUserId, Guid ToUserId, long AmountCents, string? Note);
public record RejectApprovalRequest(string? Reason);
public record RevokedInvitationsDto(int Revoked);
public record SetJoinLinkEnabledRequest(bool Enabled);
