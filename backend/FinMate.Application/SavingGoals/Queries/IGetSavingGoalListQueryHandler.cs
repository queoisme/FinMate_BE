using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Queries;

public interface IGetSavingGoalListQueryHandler
{
    Task<IReadOnlyList<SavingGoalDto>> HandleAsync(GetSavingGoalListQuery query, CancellationToken ct = default);
}
