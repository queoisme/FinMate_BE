using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

public sealed record GroupDto(
    Guid Id,
    string Name,
    GroupType Type,
    GroupTransactionVisibility TransactionVisibility,
    GroupRole MyRole,
    IReadOnlyList<GroupPermission> MyPermissions,
    int MemberCount,
    DateTimeOffset CreatedAt);

public sealed record GroupActivityDto(Guid Id, GroupActivityType Type, Guid? ActorUserId, Guid? SubjectId, DateTimeOffset CreatedAt);

public sealed record GroupActivityListResult(IReadOnlyList<GroupActivityDto> Items, string? NextCursor);

public sealed record CreateGroupCommand(Guid UserId, string Name, GroupType Type);

/// <summary>Trường null = giữ nguyên.</summary>
public sealed record UpdateGroupCommand(Guid UserId, Guid GroupId, string? Name, GroupTransactionVisibility? TransactionVisibility);

public static class GroupLimits
{
    public const int NameMaxLength = 100;
    public const int MaxActivities = 100;
}

public class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên nhóm không được để trống.")
            .MaximumLength(GroupLimits.NameMaxLength).WithMessage($"Tên nhóm tối đa {GroupLimits.NameMaxLength} ký tự.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Loại nhóm không hợp lệ.");
    }
}

public class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        When(x => x.Name is not null, () => RuleFor(x => x.Name).NotEmpty().WithMessage("Tên nhóm không được để trống.")
            .MaximumLength(GroupLimits.NameMaxLength).WithMessage($"Tên nhóm tối đa {GroupLimits.NameMaxLength} ký tự."));
        RuleFor(x => x.TransactionVisibility).IsInEnum().WithMessage("Chế độ hiển thị không hợp lệ.");
    }
}

public interface IGroupService
{
    Task<GroupDto> CreateAsync(CreateGroupCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<GroupDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<GroupDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupDto> UpdateAsync(UpdateGroupCommand command, CancellationToken ct = default);
    Task DeleteAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupActivityListResult> ListActivitiesAsync(Guid groupId, Guid userId, string? cursor, int limit, CancellationToken ct = default);
}

public class GroupService : IGroupService
{
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;
    private readonly IValidator<CreateGroupCommand> _createValidator;
    private readonly IValidator<UpdateGroupCommand> _updateValidator;

    public GroupService(
        IGroupRepository groups,
        IGroupAccessService access,
        ISystemConfigProvider config,
        IAuditLogService audit,
        IValidator<CreateGroupCommand> createValidator,
        IValidator<UpdateGroupCommand> updateValidator)
    {
        _groups = groups;
        _access = access;
        _config = config;
        _audit = audit;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<GroupDto> CreateAsync(CreateGroupCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        var settings = (await _config.GetAsync(ct)).Document.Groups;
        if (await _groups.CountGroupsForUserAsync(command.UserId, ct) >= settings.MaxGroupsPerUser)
        {
            throw new BusinessRuleException(GroupErrorCodes.TooManyGroups, $"Mỗi người tham gia tối đa {settings.MaxGroupsPerUser} nhóm.");
        }

        var now = DateTimeOffset.UtcNow;
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            Type = command.Type,
            CreatedByUserId = command.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var owner = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = command.UserId,
            Role = GroupRole.Owner,
            JoinedAt = now,
            UpdatedAt = now,
        };

        _groups.AddGroup(group);
        _groups.AddMember(owner);
        _groups.AddActivity(NewActivity(group.Id, command.UserId, GroupActivityType.GroupCreated, null, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupCreated, command.UserId, metadata: new { GroupId = group.Id }, ct: ct);
        return ToDto(group, owner, memberCount: 1);
    }

    public async Task<IReadOnlyList<GroupDto>> ListAsync(Guid userId, CancellationToken ct = default)
        => (await _groups.ListForUserAsync(userId, ct)).Select(g => ToDto(g.Group, g.Member, g.MemberCount)).ToList();

    public async Task<GroupDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        return ToDto(access.Group, access.Member, await _groups.CountActiveMembersAsync(groupId, ct));
    }

    public async Task<GroupDto> UpdateAsync(UpdateGroupCommand command, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(command, ct);
        var access = await _access.RequireOwnerAsync(command.GroupId, command.UserId, ct);
        var now = DateTimeOffset.UtcNow;

        access.Group.Name = command.Name?.Trim() ?? access.Group.Name;
        access.Group.TransactionVisibility = command.TransactionVisibility ?? access.Group.TransactionVisibility;
        access.Group.UpdatedAt = now;

        _groups.AddActivity(NewActivity(command.GroupId, command.UserId, GroupActivityType.GroupUpdated, null, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupUpdated, command.UserId, metadata: new { command.GroupId }, ct: ct);
        return ToDto(access.Group, access.Member, await _groups.CountActiveMembersAsync(command.GroupId, ct));
    }

    public async Task DeleteAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireOwnerAsync(groupId, userId, ct);
        var now = DateTimeOffset.UtcNow;
        access.Group.DeletedAt = now;
        access.Group.UpdatedAt = now;
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupDeleted, userId, metadata: new { GroupId = groupId }, ct: ct);
    }

    public async Task<GroupActivityListResult> ListActivitiesAsync(Guid groupId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewActivity, ct);
        var page = await _groups.ListActivitiesAsync(groupId, cursor, GroupPaging.Clamp(limit, GroupLimits.MaxActivities), ct);
        return new GroupActivityListResult(
            page.Items.Select(a => new GroupActivityDto(a.Id, a.Type, a.ActorUserId, a.SubjectId, a.CreatedAt)).ToList(),
            page.NextCursor);
    }

    internal static GroupActivity NewActivity(Guid groupId, Guid? actorUserId, GroupActivityType type, Guid? subjectId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = actorUserId,
            Type = type,
            SubjectId = subjectId,
            CreatedAt = now,
        };

    private static GroupDto ToDto(Group group, GroupMember member, int memberCount)
        => new(
            group.Id,
            group.Name,
            group.Type,
            group.TransactionVisibility,
            member.Role,
            GroupPermissions.Expand(GroupPermissions.Effective(member.Role, group.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked)),
            memberCount,
            group.CreatedAt);
}
