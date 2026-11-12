using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class ContributeToGoalCommandHandler : IContributeToGoalCommandHandler
{
    /// <summary>Mốc % còn lại báo một lần duy nhất khi vừa vượt qua (100% đã có thông báo hoàn thành riêng, xem <c>justCompleted</c>).</summary>
    private const int MilestonePercent = 50;

    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IValidator<ContributeToGoalCommand> _validator;
    private readonly IUserNotificationService? _userNotifications;

    public ContributeToGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IPushNotificationService pushNotificationService,
        IValidator<ContributeToGoalCommand> validator,
        IUserNotificationService? userNotifications = null)
    {
        _savingGoalRepository = savingGoalRepository;
        _pushNotificationService = pushNotificationService;
        _validator = validator;
        _userNotifications = userNotifications;
    }

    public async Task<SavingGoalDto> HandleAsync(ContributeToGoalCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var goal = await _savingGoalRepository.GetByIdAsync(command.GoalId, command.UserId, ct)
            ?? throw new NotFoundException("SavingGoal", command.GoalId);

        if (goal.Status != SavingGoalStatus.Active)
        {
            throw new BusinessRuleException(
                SavingGoalErrorCodes.NotActive,
                "Chỉ đóng góp được vào mục tiêu đang hoạt động.");
        }

        var now = DateTimeOffset.UtcNow;

        // Bookkeeping thuần: không trừ FinancialAccount.BalanceCents và không tạo Transaction.
        // Tiền vẫn nằm trong tài khoản — goal chỉ theo dõi phần user tự đánh dấu là để dành,
        // nên chi tiêu tháng không bị tính trùng vào budget.
        var contribution = new GoalContribution
        {
            Id = Guid.NewGuid(),
            SavingGoalId = goal.Id,
            UserId = command.UserId,
            AmountCents = command.AmountCents,
            Note = command.Note,
            ContributedAt = command.ContributedAt ?? now,
            CreatedAt = now,
        };

        var previousSavedCents = goal.SavedCents;
        goal.SavedCents += command.AmountCents;
        goal.UpdatedAt = now;

        var justCompleted = goal.SavedCents >= goal.TargetCents;
        if (justCompleted)
        {
            goal.Status = SavingGoalStatus.Completed;
            goal.CompletedAt = now;
        }

        // Vượt mốc đúng MỘT lần: chỉ báo khi lượt góp này là lượt làm số đã để dành vượt mốc,
        // không báo lại ở các lượt góp sau (đã qua mốc từ trước).
        var justCrossedMilestone = !justCompleted && goal.TargetCents > 0
            && previousSavedCents * 100 / goal.TargetCents < MilestonePercent
            && goal.SavedCents * 100 / goal.TargetCents >= MilestonePercent;

        // goal đã được EF track (cùng scoped DbContext) — AddContributionAsync
        // gọi SaveChangesAsync 1 lần, flush tất cả atomically.
        await _savingGoalRepository.AddContributionAsync(contribution, ct);

        if (justCompleted)
        {
            const string title = "Hoàn thành mục tiêu tiết kiệm";
            var body = $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.Name}\".";
            await _pushNotificationService.NotifyAsync(command.UserId, title, body, ct: ct);
            if (_userNotifications is not null)
            {
                await _userNotifications.CreateAsync(command.UserId, UserNotificationType.GoalMilestone, title, body, "SavingGoal", goal.Id, ct);
            }
        }
        else if (justCrossedMilestone && _userNotifications is not null)
        {
            await _userNotifications.CreateAsync(
                command.UserId,
                UserNotificationType.GoalMilestone,
                "Sắp đạt mục tiêu tiết kiệm",
                $"Bạn đã để dành được {MilestonePercent}% mục tiêu \"{goal.Name}\".",
                "SavingGoal",
                goal.Id,
                ct);
        }

        return SavingGoalMapper.ToDto(goal);
    }
}
