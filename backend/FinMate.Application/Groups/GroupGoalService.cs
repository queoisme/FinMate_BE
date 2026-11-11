using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.SavingGoals;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

public sealed record GroupContributionDto(Guid Id, Guid MemberUserId, string MemberName, long AmountCents, string? Note, DateTimeOffset ContributedAt);

public sealed record GroupContributionListResult(IReadOnlyList<GroupContributionDto> Items, string? NextCursor);

public sealed record CreateGroupGoalCommand(Guid UserId, Guid GroupId, string Name, long TargetCents, DateTimeOffset? Deadline);

/// <summary>Trường null = giữ nguyên.</summary>
public sealed record UpdateGroupGoalCommand(Guid UserId, Guid GroupId, Guid GoalId, string? Name, long? TargetCents, DateTimeOffset? Deadline);

public sealed record ContributeGroupGoalCommand(Guid UserId, Guid GroupId, Guid GoalId, long AmountCents, string? Note);

public static class GroupGoalLimits
{
    public const int NameMaxLength = 200;
    public const int NoteMaxLength = 500;
    public const int MaxContributions = 100;
}

public class CreateGroupGoalCommandValidator : AbstractValidator<CreateGroupGoalCommand>
{
    public CreateGroupGoalCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên mục tiêu không được để trống.").MaximumLength(GroupGoalLimits.NameMaxLength).WithMessage($"Tên mục tiêu tối đa {GroupGoalLimits.NameMaxLength} ký tự.");
        RuleFor(x => x.TargetCents).GreaterThan(0).WithMessage("Số tiền mục tiêu phải lớn hơn 0.").LessThanOrEqualTo(GroupFinanceLimits.MaxAmountCents).WithMessage("Số tiền mục tiêu không hợp lệ.");
    }
}

public class UpdateGroupGoalCommandValidator : AbstractValidator<UpdateGroupGoalCommand>
{
    public UpdateGroupGoalCommandValidator()
    {
        When(x => x.Name is not null, () => RuleFor(x => x.Name).NotEmpty().WithMessage("Tên mục tiêu không được để trống.").MaximumLength(GroupGoalLimits.NameMaxLength).WithMessage($"Tên mục tiêu tối đa {GroupGoalLimits.NameMaxLength} ký tự."));
        When(x => x.TargetCents is not null, () => RuleFor(x => x.TargetCents).GreaterThan(0).WithMessage("Số tiền mục tiêu phải lớn hơn 0.").LessThanOrEqualTo(GroupFinanceLimits.MaxAmountCents).WithMessage("Số tiền mục tiêu không hợp lệ."));
    }
}

public class ContributeGroupGoalCommandValidator : AbstractValidator<ContributeGroupGoalCommand>
{
    public ContributeGroupGoalCommandValidator()
    {
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền góp phải lớn hơn 0.").LessThanOrEqualTo(GroupFinanceLimits.MaxAmountCents).WithMessage("Số tiền góp không hợp lệ.");
        RuleFor(x => x.Note).MaximumLength(GroupGoalLimits.NoteMaxLength).WithMessage($"Ghi chú tối đa {GroupGoalLimits.NoteMaxLength} ký tự.");
    }
}

public interface IGroupGoalService
{
    Task<IReadOnlyList<SavingGoalDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<SavingGoalDto> CreateAsync(CreateGroupGoalCommand command, CancellationToken ct = default);
    Task<SavingGoalDto> UpdateAsync(UpdateGroupGoalCommand command, CancellationToken ct = default);
    Task CancelAsync(Guid groupId, Guid userId, Guid goalId, CancellationToken ct = default);
    Task<SavingGoalDto> ContributeAsync(ContributeGroupGoalCommand command, CancellationToken ct = default);
    Task<GroupContributionListResult> ListContributionsAsync(Guid groupId, Guid userId, Guid goalId, string? cursor, int limit, CancellationToken ct = default);
}

public class GroupGoalService : IGroupGoalService
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IGroupAlertNotifier _alerts;
    private readonly IConcurrencyRetry _retry;
    private readonly IValidator<CreateGroupGoalCommand> _createValidator;
    private readonly IValidator<UpdateGroupGoalCommand> _updateValidator;
    private readonly IValidator<ContributeGroupGoalCommand> _contributeValidator;

    public GroupGoalService(
        IGroupFinanceRepository finance,
        IGroupRepository groups,
        IGroupAccessService access,
        IGroupAlertNotifier alerts,
        IConcurrencyRetry retry,
        IValidator<CreateGroupGoalCommand> createValidator,
        IValidator<UpdateGroupGoalCommand> updateValidator,
        IValidator<ContributeGroupGoalCommand> contributeValidator)
    {
        _finance = finance;
        _groups = groups;
        _access = access;
        _alerts = alerts;
        _retry = retry;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _contributeValidator = contributeValidator;
    }

    public async Task<IReadOnlyList<SavingGoalDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        return (await _finance.ListGoalsAsync(groupId, ct)).Select(SavingGoalMapper.ToDto).ToList();
    }

    public async Task<SavingGoalDto> CreateAsync(CreateGroupGoalCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageGoals, ct);
        var now = DateTimeOffset.UtcNow;
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            CreatedByUserId = command.UserId,
            Name = command.Name.Trim(),
            TargetCents = command.TargetCents,
            Deadline = command.Deadline,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _finance.AddGoal(goal);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.GoalChanged, goal.Id, now));
        await _groups.SaveChangesAsync(ct);
        return SavingGoalMapper.ToDto(goal);
    }

    public async Task<SavingGoalDto> UpdateAsync(UpdateGroupGoalCommand command, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageGoals, ct);
        var goal = await RequireGoalAsync(command.GroupId, command.GoalId, ct);
        if (goal.Status != SavingGoalStatus.Active)
        {
            throw new BusinessRuleException(GroupErrorCodes.GoalNotActive, "Chỉ sửa được mục tiêu đang hoạt động.");
        }

        var now = DateTimeOffset.UtcNow;

        goal.Name = command.Name?.Trim() ?? goal.Name;
        goal.TargetCents = command.TargetCents ?? goal.TargetCents;
        if (command.Deadline is { } deadline && deadline != goal.Deadline)
        {
            goal.Deadline = deadline;
            goal.DeadlineNotifiedAt = null;   // hạn mới thì được nhắc lại
        }

        goal.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.GoalChanged, goal.Id, now));
        await _groups.SaveChangesAsync(ct);
        return SavingGoalMapper.ToDto(goal);
    }

    public async Task CancelAsync(Guid groupId, Guid userId, Guid goalId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ManageGoals, ct);
        var goal = await RequireGoalAsync(groupId, goalId, ct);
        var now = DateTimeOffset.UtcNow;
        goal.Status = SavingGoalStatus.Cancelled;
        goal.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.GoalChanged, goalId, now));
        await _groups.SaveChangesAsync(ct);
    }

    public async Task<SavingGoalDto> ContributeAsync(ContributeGroupGoalCommand command, CancellationToken ct = default)
    {
        await _contributeValidator.ValidateAndThrowAsync(command, ct);
        var (goal, justCompleted) = await _retry.ExecuteAsync(token => ContributeOnceAsync(command, token), ct);
        if (justCompleted)
        {
            await _alerts.SendGoalAlertAsync(goal, "Hoàn thành mục tiêu của nhóm", $"Nhóm đã hoàn thành mục tiêu \"{goal.Name}\".", "completed", ct);
        }

        return SavingGoalMapper.ToDto(goal);
    }

    public async Task<GroupContributionListResult> ListContributionsAsync(Guid groupId, Guid userId, Guid goalId, string? cursor, int limit, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        await RequireGoalAsync(groupId, goalId, ct);
        var page = await _finance.ListContributionsAsync(goalId, cursor, GroupPaging.Clamp(limit, GroupGoalLimits.MaxContributions), ct);
        return new GroupContributionListResult(
            page.Items.Select(r => new GroupContributionDto(
                r.Contribution.Id,
                r.Contribution.UserId,
                r.IsUserActive ? r.MemberName : GroupMemberService.InactiveMemberName,
                r.Contribution.AmountCents,
                r.Contribution.Note,
                r.Contribution.ContributedAt)).ToList(),
            page.NextCursor);
    }

    private async Task<(SavingGoal Goal, bool JustCompleted)> ContributeOnceAsync(ContributeGroupGoalCommand command, CancellationToken ct)
    {
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ContributeGoals, ct);
        var goal = await RequireGoalAsync(command.GroupId, command.GoalId, ct);
        if (goal.Status != SavingGoalStatus.Active)
        {
            throw new BusinessRuleException(GroupErrorCodes.GoalNotActive, "Chỉ góp được vào mục tiêu đang hoạt động.");
        }

        var now = DateTimeOffset.UtcNow;

        // Ghi sổ, giống mục tiêu cá nhân: không trừ ví và không tạo giao dịch, tiền vẫn nằm ở nơi người góp đang giữ.
        _finance.AddContribution(new GoalContribution
        {
            Id = Guid.NewGuid(),
            SavingGoalId = goal.Id,
            UserId = command.UserId,
            AmountCents = command.AmountCents,
            Note = command.Note,
            ContributedAt = now,
            CreatedAt = now,
        });
        goal.SavedCents += command.AmountCents;
        goal.UpdatedAt = now;

        var justCompleted = goal.SavedCents >= goal.TargetCents;
        if (justCompleted)
        {
            goal.Status = SavingGoalStatus.Completed;
            goal.CompletedAt = now;
        }

        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.GoalContributed, goal.Id, now));
        await _groups.SaveChangesAsync(ct);
        return (goal, justCompleted);
    }

    private async Task<SavingGoal> RequireGoalAsync(Guid groupId, Guid goalId, CancellationToken ct)
        => await _finance.GetGoalAsync(groupId, goalId, ct) ?? throw new NotFoundException("SavingGoal", goalId);
}
