using FinMate.Domain.Enums;

namespace FinMate.Application.SavingGoals.Queries;

public record GetSavingGoalListQuery(Guid UserId, SavingGoalStatus? Status);
