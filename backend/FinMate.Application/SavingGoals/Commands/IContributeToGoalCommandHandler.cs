using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Commands;

public interface IContributeToGoalCommandHandler
{
    Task<SavingGoalDto> HandleAsync(ContributeToGoalCommand command, CancellationToken ct = default);
}
