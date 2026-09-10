using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public interface ISavingGoalRepository
{
    Task<SavingGoal?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<List<SavingGoal>> GetListForUserAsync(Guid userId, SavingGoalStatus? status, CancellationToken ct = default);
    Task AddAsync(SavingGoal goal, CancellationToken ct = default);
    Task UpdateAsync(SavingGoal goal, CancellationToken ct = default);

    /// <summary>
    /// Ghi contribution và flush luôn cả thay đổi đang track trên goal — cùng 1 scoped
    /// DbContext nên 2 bảng đi trong 1 DB transaction ngầm.
    /// </summary>
    Task AddContributionAsync(GoalContribution contribution, CancellationToken ct = default);

    Task<List<GoalContribution>> GetContributionsAsync(Guid goalId, Guid userId, int limit, CancellationToken ct = default);

    /// <summary>Goal còn Active, đã quá deadline và chưa được nhắc lần nào.</summary>
    Task<List<SavingGoal>> GetOverdueUnnotifiedGoalsAsync(DateTimeOffset now, CancellationToken ct = default);
}
