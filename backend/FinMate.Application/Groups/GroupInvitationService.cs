using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

/// <param name="Token">Chỉ có với lời mời qua link và chỉ trả đúng một lần lúc tạo: DB chỉ giữ hash nên không xem lại được.</param>
/// <param name="EmailSent">Với lời mời qua email: nhà cung cấp email có nhận thư không. Không liên quan tới việc địa chỉ đó đã có tài khoản hay chưa.</param>
public sealed record GroupInvitationDto(Guid Id, string? Email, GroupRole Role, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, string? Token = null, bool? EmailSent = null);

public sealed record GroupInvitationPreviewDto(string GroupName, GroupType GroupType, GroupRole Role, string? InvitedBy, DateTimeOffset ExpiresAt);

public sealed record CreateGroupInvitationCommand(Guid UserId, Guid GroupId, string? Email, GroupRole Role);

public class CreateGroupInvitationCommandValidator : AbstractValidator<CreateGroupInvitationCommand>
{
    private const int EmailMaxLength = 320;

    public CreateGroupInvitationCommandValidator()
    {
        RuleFor(x => x.Role).IsInEnum().NotEqual(GroupRole.Owner).WithMessage("Vai trò được mời không hợp lệ.");
        When(x => x.Email is not null, () => RuleFor(x => x.Email!)
            .EmailAddress().WithMessage("Email không hợp lệ.")
            .MaximumLength(EmailMaxLength).WithMessage("Email không hợp lệ."));
    }
}

public interface IGroupInvitationService
{
    Task<GroupInvitationDto> CreateAsync(CreateGroupInvitationCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<GroupInvitationDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task RevokeAsync(Guid groupId, Guid userId, Guid invitationId, CancellationToken ct = default);
    Task<GroupInvitationPreviewDto> PreviewAsync(Guid userId, string token, CancellationToken ct = default);
    Task<Guid> AcceptAsync(Guid userId, string token, CancellationToken ct = default);
}

public class GroupInvitationService : IGroupInvitationService
{
    private const int TokenBytes = 32;

    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IUserRepository _users;
    private readonly IEmailSender _email;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;
    private readonly IValidator<CreateGroupInvitationCommand> _validator;

    public GroupInvitationService(
        IGroupRepository groups,
        IGroupAccessService access,
        IUserRepository users,
        IEmailSender email,
        ISystemConfigProvider config,
        IAuditLogService audit,
        IValidator<CreateGroupInvitationCommand> validator)
    {
        _groups = groups;
        _access = access;
        _users = users;
        _email = email;
        _config = config;
        _audit = audit;
        _validator = validator;
    }

    public async Task<GroupInvitationDto> CreateAsync(CreateGroupInvitationCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var access = await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.InviteMembers, ct);
        if (!GroupPermissions.Outranks(access.Member.Role, command.Role))
        {
            throw new ForbiddenException("Bạn chỉ mời được người có vai trò thấp hơn mình.");
        }

        var settings = (await _config.GetAsync(ct)).Document.Groups;
        var now = DateTimeOffset.UtcNow;
        if (await _groups.CountPendingInvitationsAsync(command.GroupId, now, ct) >= settings.MaxPendingInvitations)
        {
            throw new BusinessRuleException(GroupErrorCodes.TooManyInvitations, $"Nhóm chỉ có tối đa {settings.MaxPendingInvitations} lời mời đang chờ.");
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var invitation = new GroupInvitation
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            TokenHash = HashToken(token),
            Email = NormalizeEmail(command.Email),
            Role = command.Role,
            ExpiresAt = now.AddDays(settings.InvitationTtlDays),
            CreatedByUserId = command.UserId,
            CreatedAt = now,
        };

        _groups.AddInvitation(invitation);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.InvitationCreated, invitation.Id, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupInvitationCreated, command.UserId, metadata: new { command.GroupId, InvitationId = invitation.Id }, ct: ct);

        if (invitation.Email is null)
        {
            return ToDto(invitation) with { Token = token };
        }

        var sent = await _email.SendAsync(invitation.Email, $"Lời mời tham gia nhóm \"{access.Group.Name}\" trên FinMate", InvitationEmailBody(access.Group.Name, token, settings.InvitationTtlDays), ct);
        return ToDto(invitation) with { EmailSent = sent };
    }

    public async Task<IReadOnlyList<GroupInvitationDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        return (await _groups.ListPendingInvitationsAsync(groupId, DateTimeOffset.UtcNow, ct)).Select(i => ToDto(i)).ToList();
    }

    public async Task RevokeAsync(Guid groupId, Guid userId, Guid invitationId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        var invitation = await _groups.GetInvitationAsync(groupId, invitationId, ct) ?? throw new NotFoundException("GroupInvitation", invitationId);
        var now = DateTimeOffset.UtcNow;
        if (!invitation.IsPending(now))
        {
            return;   // đã hết hiệu lực: thu hồi lần nữa không có gì để làm
        }

        invitation.RevokedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.InvitationRevoked, invitationId, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupInvitationRevoked, userId, metadata: new { GroupId = groupId, InvitationId = invitationId }, ct: ct);
    }

    public async Task<GroupInvitationPreviewDto> PreviewAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var (invitation, group, _) = await RequireUsableAsync(userId, token, ct);
        var inviter = invitation.CreatedByUserId is { } inviterId ? await _users.GetByIdAsync(inviterId, ct) : null;
        return new GroupInvitationPreviewDto(group.Name, group.Type, invitation.Role, inviter?.DisplayName, invitation.ExpiresAt);
    }

    public async Task<Guid> AcceptAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var (invitation, group, user) = await RequireUsableAsync(userId, token, ct);
        var existing = await _groups.GetMemberAsync(group.Id, user.Id, ct);
        if (existing is { LeftAt: null })
        {
            throw new ConflictException(GroupErrorCodes.AlreadyMember, "Bạn đã là thành viên của nhóm này.");
        }

        await EnsureCapacityAsync(group.Id, user.Id, ct);

        var now = DateTimeOffset.UtcNow;
        Join(existing, invitation, user.Id, now);
        invitation.AcceptedByUserId = user.Id;
        invitation.AcceptedAt = now;

        _groups.AddActivity(GroupService.NewActivity(group.Id, user.Id, GroupActivityType.MemberJoined, user.Id, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupMemberJoined, user.Id, metadata: new { GroupId = group.Id, InvitationId = invitation.Id }, ct: ct);
        return group.Id;
    }

    private void Join(GroupMember? previous, GroupInvitation invitation, Guid userId, DateTimeOffset now)
    {
        if (previous is null)
        {
            _groups.AddMember(new GroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = invitation.GroupId,
                UserId = userId,
                Role = invitation.Role,
                JoinedAt = now,
                UpdatedAt = now,
            });
            return;
        }

        // Quyền ghi đè của lần tham gia trước không được theo người đó quay lại.
        previous.Role = invitation.Role;
        previous.PermissionsGranted = GroupPermission.None;
        previous.PermissionsRevoked = GroupPermission.None;
        previous.AlertsEnabled = true;
        previous.JoinedAt = now;
        previous.LeftAt = null;
        previous.UpdatedAt = now;
    }

    private async Task EnsureCapacityAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var settings = (await _config.GetAsync(ct)).Document.Groups;
        if (await _groups.CountActiveMembersAsync(groupId, ct) >= settings.MaxMembers)
        {
            throw new BusinessRuleException(GroupErrorCodes.TooManyMembers, $"Nhóm đã đủ {settings.MaxMembers} thành viên.");
        }

        if (await _groups.CountGroupsForUserAsync(userId, ct) >= settings.MaxGroupsPerUser)
        {
            throw new BusinessRuleException(GroupErrorCodes.TooManyGroups, $"Mỗi người tham gia tối đa {settings.MaxGroupsPerUser} nhóm.");
        }
    }

    /// <summary>Mọi lý do mã không dùng được đều trả cùng một lỗi: người cầm mã không phân biệt được mã sai, hết hạn hay dành cho email khác.</summary>
    private async Task<(GroupInvitation Invitation, Group Group, User User)> RequireUsableAsync(Guid userId, string token, CancellationToken ct)
    {
        var invitation = await _groups.GetInvitationByTokenHashAsync(HashToken(token), ct);
        var group = invitation is null ? null : await _groups.GetByIdAsync(invitation.GroupId, ct);
        var user = await _users.GetByIdAsync(userId, ct);
        if (invitation is null || group is null || user is null || !invitation.IsPending(DateTimeOffset.UtcNow) || !MatchesEmail(invitation, user))
        {
            throw new BusinessRuleException(GroupErrorCodes.InvitationInvalid, "Lời mời không hợp lệ hoặc đã hết hạn.");
        }

        return (invitation, group, user);
    }

    private static bool MatchesEmail(GroupInvitation invitation, User user)
        => invitation.Email is null
            || (user.EmailVerifiedAt is not null && string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant();

    private static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string InvitationEmailBody(string groupName, string token, int ttlDays)
        => $"Bạn được mời tham gia nhóm \"{groupName}\" trên FinMate.\n\n"
            + $"Mở FinMate, vào mục Nhóm, chọn Nhập mã mời và dán mã sau:\n\n{token}\n\n"
            + $"Mã có hiệu lực trong {ttlDays} ngày và chỉ dùng được một lần. Nếu bạn không mong đợi lời mời này, hãy bỏ qua thư.";

    private static GroupInvitationDto ToDto(GroupInvitation invitation)
        => new(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, invitation.CreatedAt);
}
