using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class ContributeToGoalCommandHandler : IContributeToGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IValidator<ContributeToGoalCommand> _validator;

    public ContributeToGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IPushNotificationService pushNotificationService,
        IValidator<ContributeToGoalCommand> validator)
    {
        _savingGoalRepository = savingGoalRepository;
        _pushNotificationService = pushNotificationService;
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

        // goal đã được EF track từ GetByIdAsync (cùng scoped DbContext) — AddContributionAsync
        // gọi SaveChangesAsync 1 lần, flush cả contribution lẫn goal atomically.
        await _savingGoalRepository.AddContributionAsync(contribution, ct);

        if (justCompleted)
        {
            await _pushNotificationService.NotifyAsync(
                command.UserId,
                "Hoàn thành mục tiêu tiết kiệm",
                $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.Name}\".",
                ct);

            // TODO [!] Blocked by Phase 7: trigger Mascot celebration — Gamification module chưa tồn tại.
        }

        return SavingGoalMapper.ToDto(goal);
    }
}
