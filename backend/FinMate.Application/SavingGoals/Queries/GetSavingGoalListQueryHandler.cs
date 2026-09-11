using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Queries;

public class GetSavingGoalListQueryHandler : IGetSavingGoalListQueryHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;

    public GetSavingGoalListQueryHandler(ISavingGoalRepository savingGoalRepository)
    {
        _savingGoalRepository = savingGoalRepository;
    }

    public async Task<IReadOnlyList<SavingGoalDto>> HandleAsync(
        GetSavingGoalListQuery query,
        CancellationToken ct = default)
    {
        var goals = await _savingGoalRepository.GetListForUserAsync(query.UserId, query.Status, ct);
        return goals.Select(g => SavingGoalMapper.ToDto(g)).ToList();
    }
}
