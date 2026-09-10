using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Queries;

public interface IGetGoalProgressQueryHandler
{
    Task<GoalProgressDto> HandleAsync(GetGoalProgressQuery query, CancellationToken ct = default);
}
