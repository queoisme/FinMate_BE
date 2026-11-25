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
    DateTimeOffset CreatedAt,
    string? Icon = null,
    string? AvatarUrl = null,
    string? Description = null,
    string Currency = "VND",
    GroupSettlementModel SettlementModel = GroupSettlementModel.SharedFund,
    int? ClosingDay = null,
    DebtSettlementMethod DebtSettlementMethod = DebtSettlementMethod.AiOptimized,
    Guid? MyDefaultFinancialAccountId = null,
    GroupCardDto? Card = null);

/// <summary>Số liệu thẻ danh sách. Chỉ có ở <c>GET /groups</c>. <c>FundBalanceCents</c> null khi chưa có quỹ; <c>MonthlyBudgetCents</c> null khi chưa có ngân sách tổng.</summary>
public sealed record GroupCardDto(
    long? FundBalanceCents,
    long MonthlySpentCents,
    long? MonthlyBudgetCents,
    IReadOnlyList<GroupMemberPreviewDto> MemberPreview);

public sealed record GroupMemberPreviewDto(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record GroupActivityDto(Guid Id, GroupActivityType Type, Guid? ActorUserId, Guid? SubjectId, DateTimeOffset CreatedAt);

public sealed record GroupActivityListResult(IReadOnlyList<GroupActivityDto> Items, string? NextCursor);

public sealed record GroupProfile(
    string? Icon = null,
    string? Description = null,
    string? Currency = null,
    GroupSettlementModel? SettlementModel = null,
    int? ClosingDay = null,
    DebtSettlementMethod? DebtSettlementMethod = null);

public sealed record CreateGroupCommand(Guid UserId, string Name, GroupType Type, GroupProfile? Profile = null);

/// <summary>Trường null = giữ nguyên.</summary>
public sealed record UpdateGroupCommand(Guid UserId, Guid GroupId, string? Name, GroupTransactionVisibility? TransactionVisibility, GroupProfile? Profile = null);

public static class GroupLimits
{
    public const int NameMaxLength = 100;
    public const int MaxActivities = 100;
    public const int IconMaxLength = 50;
    public const int DescriptionMaxLength = 200;
    public const int MemberPreviewCount = 3;
}

internal static class GroupProfileRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, GroupProfile?> profile)
    {
        v.RuleFor(x => profile(x)!.Icon).MaximumLength(GroupLimits.IconMaxLength).WithMessage($"Biểu tượng tối đa {GroupLimits.IconMaxLength} ký tự.")
            .When(x => profile(x)?.Icon is not null);
        v.RuleFor(x => profile(x)!.Description).MaximumLength(GroupLimits.DescriptionMaxLength).WithMessage($"Mô tả tối đa {GroupLimits.DescriptionMaxLength} ký tự.")
            .When(x => profile(x)?.Description is not null);
        v.RuleFor(x => profile(x)!.Currency).Must(c => c == "VND").WithMessage("Hiện chỉ hỗ trợ tiền tệ VND.")
            .When(x => profile(x)?.Currency is not null);
        v.RuleFor(x => profile(x)!.ClosingDay!.Value).InclusiveBetween(1, 28).WithMessage("Ngày chốt sổ phải từ 1 đến 28.")
            .When(x => profile(x)?.ClosingDay is not null);
        v.RuleFor(x => profile(x)!.SettlementModel!.Value).IsInEnum().WithMessage("Mô hình chia tiền không hợp lệ.")
            .When(x => profile(x)?.SettlementModel is not null);
        v.RuleFor(x => profile(x)!.DebtSettlementMethod!.Value).IsInEnum().WithMessage("Cách cấn trừ nợ không hợp lệ.")
            .When(x => profile(x)?.DebtSettlementMethod is not null);
    }
}

public class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên nhóm không được để trống.")
            .MaximumLength(GroupLimits.NameMaxLength).WithMessage($"Tên nhóm tối đa {GroupLimits.NameMaxLength} ký tự.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Loại nhóm không hợp lệ.");
        GroupProfileRules.Apply(this, c => c.Profile);
    }
}

public class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        When(x => x.Name is not null, () => RuleFor(x => x.Name).NotEmpty().WithMessage("Tên nhóm không được để trống.")
            .MaximumLength(GroupLimits.NameMaxLength).WithMessage($"Tên nhóm tối đa {GroupLimits.NameMaxLength} ký tự."));
        RuleFor(x => x.TransactionVisibility).IsInEnum().WithMessage("Chế độ hiển thị không hợp lệ.");
        GroupProfileRules.Apply(this, c => c.Profile);
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
    private readonly IGroupFundRepository? _funds;
    private readonly IGroupFinanceRepository? _finance;
    private readonly IGroupReportService? _reports;

    public GroupService(
        IGroupRepository groups,
        IGroupAccessService access,
        ISystemConfigProvider config,
        IAuditLogService audit,
        IValidator<CreateGroupCommand> createValidator,
        IValidator<UpdateGroupCommand> updateValidator,
        IGroupFundRepository? funds = null,
        IGroupFinanceRepository? finance = null,
        IGroupReportService? reports = null)
    {
        _funds = funds;
        _finance = finance;
        _reports = reports;
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
            Icon = Clean(command.Profile?.Icon),
            Description = Clean(command.Profile?.Description),
            SettlementModel = command.Profile?.SettlementModel ?? GroupSettlementModel.SharedFund,
            ClosingDay = command.Profile?.ClosingDay,
            DebtSettlementMethod = command.Profile?.DebtSettlementMethod ?? DebtSettlementMethod.AiOptimized,
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
    {
        var rows = await _groups.ListForUserAsync(userId, ct);
        var result = new List<GroupDto>(rows.Count);
        foreach (var g in rows)
        {
            result.Add(ToDto(g.Group, g.Member, g.MemberCount) with { Card = await BuildCardAsync(g.Group.Id, userId, ct) });
        }

        return result;
    }

    private async Task<GroupCardDto?> BuildCardAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        long? fund = null;
        if (_funds is not null)
        {
            var open = (await _funds.ListAsync(groupId, ct)).Where(f => !f.IsArchived).ToList();
            fund = open.Count == 0 ? null : open.Sum(f => f.BalanceCents);
        }

        var spent = 0L;
        if (_reports is not null)
        {
            var (year, month) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
            spent = (await _reports.GetMonthlyAsync(groupId, userId, year, month, ct)).TotalSpentCents;
        }

        long? budget = null;
        if (_finance is not null)
        {
            budget = (await _finance.ListBudgetsAsync(groupId, ct)).FirstOrDefault(b => b.CategoryId is null && b.GroupCategoryId is null)?.LimitCents;
        }

        var preview = (await _groups.ListActiveMembersAsync(groupId, ct)).Take(GroupLimits.MemberPreviewCount)
            .Select(m => new GroupMemberPreviewDto(m.Member.UserId, m.IsUserActive ? m.DisplayName : GroupMemberService.InactiveMemberName, null))
            .ToList();
        return new GroupCardDto(fund, spent, budget, preview);
    }

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
        if (command.Profile is { } p)
        {
            access.Group.Icon = p.Icon is null ? access.Group.Icon : Clean(p.Icon);
            access.Group.Description = p.Description is null ? access.Group.Description : Clean(p.Description);
            access.Group.SettlementModel = p.SettlementModel ?? access.Group.SettlementModel;
            access.Group.ClosingDay = p.ClosingDay ?? access.Group.ClosingDay;
            access.Group.DebtSettlementMethod = p.DebtSettlementMethod ?? access.Group.DebtSettlementMethod;
        }
        access.Group.UpdatedAt = now;

        _groups.AddActivity(NewActivity(command.GroupId, command.UserId, GroupActivityType.GroupUpdated, null, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupUpdated, command.UserId, metadata: new { command.GroupId }, ct: ct);
        return ToDto(access.Group, access.Member, await _groups.CountActiveMembersAsync(command.GroupId, ct));
    }

    public async Task DeleteAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireOwnerAsync(groupId, userId, ct);

        // BR-FUND-03 (D17): quỹ chưa lưu trữ là tiền các thành viên đang giữ hộ nhau; xoá nhóm thì sổ đó không còn ai xem được.
        if (_funds is not null && await _funds.CountActiveAsync(groupId, ct) > 0)
        {
            throw new BusinessRuleException(GroupErrorCodes.FundNotSettled, "Nhóm còn quỹ chưa lưu trữ. Hãy đưa các quỹ về 0 và lưu trữ trước khi xoá nhóm.");
        }

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
            group.CreatedAt,
            group.Icon,
            group.AvatarUrl,
            group.Description,
            group.Currency,
            group.SettlementModel,
            group.ClosingDay,
            group.DebtSettlementMethod,
            member.DefaultFinancialAccountId);

    /// <summary>Chuỗi rỗng sau khi cắt khoảng trắng là xóa giá trị.</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
