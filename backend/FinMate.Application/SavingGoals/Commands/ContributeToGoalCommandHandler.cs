using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Application.Gamification;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class ContributeToGoalCommandHandler : IContributeToGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IGamificationService _gamificationService;
    private readonly ICacheService _cache;
    private readonly IValidator<ContributeToGoalCommand> _validator;

    public ContributeToGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IPushNotificationService pushNotificationService,
        IGamificationService gamificationService,
        ICacheService cache,
        IValidator<ContributeToGoalCommand> validator)
    {
        _savingGoalRepository = savingGoalRepository;
        _pushNotificationService = pushNotificationService;
        _gamificationService = gamificationService;
        _cache = cache;
        _validator = validator;
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

        goal.SavedCents += command.AmountCents;
        goal.UpdatedAt = now;

        var justCompleted = goal.SavedCents >= goal.TargetCents;
        if (justCompleted)
        {
            goal.Status = SavingGoalStatus.Completed;
            goal.CompletedAt = now;
        }

        var outcome = await _gamificationService.RecordActivityAsync(
            new GamificationActivity(
                command.UserId,
                MissionConditionType.ContributeToGoal,
                GoalExpRewards.Contribute,
                now),
            ct);

        // goal và gamification đã được EF track (cùng scoped DbContext) — AddContributionAsync
        // gọi SaveChangesAsync 1 lần, flush tất cả atomically.
        await _savingGoalRepository.AddContributionAsync(contribution, ct);

        await GamificationCache.InvalidateAsync(_cache, command.UserId, ct);

        if (justCompleted)
        {
            await _pushNotificationService.NotifyAsync(
                command.UserId,
                "Hoàn thành mục tiêu tiết kiệm",
                $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.Name}\".",
                ct: ct);
        }

        // Mascot ăn mừng: item vừa mở khóa và việc lên level đi kèm trong response để client
        // diễn hoạt ngay, thay vì phải gọi thêm một vòng /gamification/mascot mới biết.
        return SavingGoalMapper.ToDto(goal, new GoalCelebrationDto(
            outcome.LeveledUp,
            outcome.Level,
            outcome.UnlockedItems.Select(i => i.Name).ToList()));
    }
}
