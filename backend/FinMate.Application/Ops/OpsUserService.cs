using FinMate.Application.Admin.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Groups;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Ops;

/// <summary>Thông tin tối thiểu để xử lý ca. Không có dữ liệu tài chính, và email đã che một phần.</summary>
public record OpsUserDto(Guid Id, string DisplayName, string MaskedEmail, string Role, bool IsLocked, bool DeletionPending, DateTimeOffset CreatedAt);

public record OpsGroupMemberDto(Guid UserId, string DisplayName, string Role, DateTimeOffset JoinedAt);

public record OpsGroupActivityDto(string Type, Guid? ActorUserId, DateTimeOffset CreatedAt);

/// <summary>Chỉ thành viên, vai trò và loại hoạt động. Không có giao dịch hay số tiền.</summary>
public record OpsGroupDto(Guid Id, string Name, string Type, IReadOnlyList<OpsGroupMemberDto> Members, IReadOnlyList<OpsGroupActivityDto> Activities);

public interface IOpsUserService
{
    Task<OpsUserDto> GetUserAsync(Guid actorId, Guid userId, CancellationToken ct = default);
    Task<OpsUserDto> EmergencyLockAsync(Guid actorId, Guid userId, Guid ticketId, string? reason, string? ip, CancellationToken ct = default);
    Task<OpsGroupDto> GetGroupAsync(Guid actorId, Guid groupId, Guid ticketId, string? ip, CancellationToken ct = default);
}

public sealed class OpsUserService : IOpsUserService
{
    private const int MaxReasonLength = 1000;
    private const int ActivityLimit = 50;

    private readonly IOpsAccessService _access;
    private readonly IUserRepository _users;
    private readonly ISupportRepository _support;
    private readonly IGroupRepository _groups;
    private readonly ISetUserLockCommandHandler _lockHandler;
    private readonly IDataDeletionRequestRepository _deletions;
    private readonly IAuditLogService _audit;

    public OpsUserService(
        IOpsAccessService access,
        IUserRepository users,
        ISupportRepository support,
        IGroupRepository groups,
        ISetUserLockCommandHandler lockHandler,
        IDataDeletionRequestRepository deletions,
        IAuditLogService audit)
    {
        _access = access;
        _users = users;
        _support = support;
        _groups = groups;
        _lockHandler = lockHandler;
        _deletions = deletions;
        _audit = audit;
    }

    public async Task<OpsUserDto> GetUserAsync(Guid actorId, Guid userId, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var canSeeAnyone = OpsPermissions.For(actor.Role).HasFlag(OpsCapability.ManageTickets);
        if (!canSeeAnyone && !await _support.IsHandlingRequesterAsync(actor.Id, userId, ct))
        {
            throw new ForbiddenException("Bạn không phụ trách yêu cầu nào của tài khoản này.");
        }

        var user = await _users.GetByIdIncludingDeletedAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        var deletionPending = await _deletions.GetPendingForUserAsync(userId, ct) is not null;
        return new OpsUserDto(user.Id, user.DisplayName, MaskEmail(user.Email), user.Role.ToString(), user.IsLocked, deletionPending, user.CreatedAt);
    }

    public async Task<OpsUserDto> EmergencyLockAsync(Guid actorId, Guid userId, Guid ticketId, string? reason, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.EmergencyLock, ct);
        var cleanReason = SupportTicketRules.RequireText(reason, MaxReasonLength, "lý do");
        var ticket = await RequireHandledTicketAsync(actor, ticketId, ct);

        // Khóa được đúng người tạo ca: nhân viên không khóa được một tài khoản bất kỳ.
        if (ticket.RequesterUserId != userId)
        {
            throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Chỉ khóa được tài khoản của người tạo yêu cầu này.");
        }

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        if (target.Role != UserRole.User)
        {
            throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Không khóa khẩn cấp được tài khoản nội bộ. Hãy báo Admin.");
        }

        await _lockHandler.HandleAsync(new SetUserLockCommand(actor.Id, userId, true, $"Khóa khẩn cấp theo yêu cầu {ticketId}", ip), ct);

        // Đẩy ca lên cho Manager rà soát: khóa làm ngay, kiểm tra làm sau.
        var now = DateTimeOffset.UtcNow;
        _support.AddMessage(new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = actor.Id,
            Kind = SupportMessageKind.InternalNote,
            Body = $"Đã khóa khẩn cấp tài khoản. Lý do: {cleanReason}",
            CreatedAt = now,
        });
        if (ticket.Status != SupportTicketStatus.Closed)
        {
            ticket.Status = SupportTicketStatus.Escalated;
        }

        ticket.UpdatedAt = now;
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsUserEmergencyLocked, actor.Id, ip, new { targetUserId = userId, ticketId }, ct);

        return new OpsUserDto(target.Id, target.DisplayName, MaskEmail(target.Email), target.Role.ToString(), true, false, target.CreatedAt);
    }

    public async Task<OpsGroupDto> GetGroupAsync(Guid actorId, Guid groupId, Guid ticketId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        var ticket = await RequireHandledTicketAsync(actor, ticketId, ct);

        // Người tạo ca phải là thành viên của nhóm, kể cả đã rời: chủ nhóm có thể chính là lý do họ bị đẩy ra.
        var requesterMembership = await _groups.GetMemberAsync(groupId, ticket.RequesterUserId, ct);
        var group = await _groups.GetByIdAsync(groupId, ct);
        if (group is null || requesterMembership is null)
        {
            throw new NotFoundException("Group", groupId);
        }

        var members = await _groups.ListActiveMembersAsync(groupId, ct);
        var activities = await _groups.ListActivitiesAsync(groupId, ActivityLimit, ct);
        await _audit.LogAsync(AuditEvents.OpsGroupViewed, actor.Id, ip, new { groupId, ticketId }, ct);

        return new OpsGroupDto(
            group.Id,
            group.Name,
            group.Type.ToString(),
            members.Select(m => new OpsGroupMemberDto(
                m.Member.UserId,
                m.IsUserActive ? m.DisplayName : GroupMemberService.InactiveMemberName,
                m.Member.Role.ToString(),
                m.Member.JoinedAt)).ToList(),
            activities.Select(a => new OpsGroupActivityDto(a.Type.ToString(), a.ActorUserId, a.CreatedAt)).ToList());
    }

    private async Task<SupportTicket> RequireHandledTicketAsync(User actor, Guid ticketId, CancellationToken ct)
    {
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        if (ticket.RequesterUserId == actor.Id
            || (ticket.AssigneeUserId != actor.Id && !OpsPermissions.For(actor.Role).HasFlag(OpsCapability.ManageTickets)))
        {
            throw new ForbiddenException("Bạn không phụ trách yêu cầu này.");
        }

        return ticket;
    }

    /// <summary><c>an.nguyen@gmail.com</c> thành <c>an***@gmail.com</c>.</summary>
    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        return $"{email[..Math.Min(2, at)]}***{email[at..]}";
    }
}
