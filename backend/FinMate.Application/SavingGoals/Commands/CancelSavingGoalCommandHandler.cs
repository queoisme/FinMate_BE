using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.Application.SavingGoals.Commands;

public class CancelSavingGoalCommandHandler : ICancelSavingGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;

    public CancelSavingGoalCommandHandler(ISavingGoalRepository savingGoalRepository)
    {
        _savingGoalRepository = savingGoalRepository;
    }

    public async Task<SavingGoalDto> HandleAsync(CancelSavingGoalCommand command, CancellationToken ct = default)
    {
        var goal = await _savingGoalRepository.GetByIdAsync(command.GoalId, command.UserId, ct)
            ?? throw new NotFoundException("SavingGoal", command.GoalId);

        if (goal.Status != SavingGoalStatus.Active)
        {
            throw new BusinessRuleException(
                SavingGoalErrorCodes.NotActive,
                "Chỉ hủy được mục tiêu đang hoạt động.");
        }

        var now = DateTimeOffset.UtcNow;
        goal.Status = SavingGoalStatus.Cancelled;
        goal.UpdatedAt = now;

        // Giữ nguyên saved_cents và goal_contributions — hủy mục tiêu không xóa lịch sử
        // đã đóng góp, user vẫn xem lại được.
        await _savingGoalRepository.UpdateAsync(goal, ct);

        return SavingGoalMapper.ToDto(goal);
    }
}
