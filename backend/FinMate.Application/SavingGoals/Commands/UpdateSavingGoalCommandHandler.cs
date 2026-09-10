using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class UpdateSavingGoalCommandHandler : IUpdateSavingGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IValidator<UpdateSavingGoalCommand> _validator;

    public UpdateSavingGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IValidator<UpdateSavingGoalCommand> validator)
    {
        _savingGoalRepository = savingGoalRepository;
        _validator = validator;
    }

    public async Task<SavingGoalDto> HandleAsync(UpdateSavingGoalCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var goal = await _savingGoalRepository.GetByIdAsync(command.GoalId, command.UserId, ct)
            ?? throw new NotFoundException("SavingGoal", command.GoalId);

        if (goal.Status != SavingGoalStatus.Active)
        {
            throw new BusinessRuleException(
                SavingGoalErrorCodes.NotActive,
                "Chỉ sửa được mục tiêu đang hoạt động.");
        }

        var now = DateTimeOffset.UtcNow;
        goal.Name = command.Name;
        goal.TargetCents = command.TargetCents;
        goal.Deadline = command.Deadline;
        goal.UpdatedAt = now;

        // Hạ mục tiêu xuống dưới số đã tiết kiệm được thì coi như đã hoàn thành.
        if (goal.SavedCents >= goal.TargetCents)
        {
            goal.Status = SavingGoalStatus.Completed;
            goal.CompletedAt = now;
        }

        await _savingGoalRepository.UpdateAsync(goal, ct);

        return SavingGoalMapper.ToDto(goal);
    }
}
