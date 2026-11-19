using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.Application.SavingGoals.Commands;

public interface IActivateSavingGoalCommandHandler
{
    Task<SavingGoalDto> HandleAsync(Guid userId, Guid goalId, CancellationToken ct = default);
}

/// <summary>Bản nháp thành mục tiêu đang hoạt động. Kiểm lại hạn chót vì nháp có thể đã nằm lâu: hạn đã qua thì người dùng phải sửa trước.</summary>
public class ActivateSavingGoalCommandHandler : IActivateSavingGoalCommandHandler
{
    private readonly ISavingGoalRepository _goals;

    public ActivateSavingGoalCommandHandler(ISavingGoalRepository goals) => _goals = goals;

    public async Task<SavingGoalDto> HandleAsync(Guid userId, Guid goalId, CancellationToken ct = default)
    {
        var goal = await _goals.GetByIdAsync(goalId, userId, ct) ?? throw new NotFoundException("SavingGoal", goalId);

        if (goal.Status != SavingGoalStatus.Draft)
        {
            throw new BusinessRuleException(SavingGoalErrorCodes.NotDraft, "Chỉ kích hoạt được mục tiêu đang là bản nháp.");
        }

        var now = DateTimeOffset.UtcNow;
        if (goal.Deadline is { } deadline && deadline <= now)
        {
            throw new BusinessRuleException(SavingGoalErrorCodes.DeadlinePassed, "Hạn hoàn thành đã qua, hãy sửa hạn trước khi kích hoạt.");
        }

        goal.Status = SavingGoalStatus.Active;
        goal.UpdatedAt = now;
        await _goals.UpdateAsync(goal, ct);
        return SavingGoalMapper.ToDto(goal);
    }
}
