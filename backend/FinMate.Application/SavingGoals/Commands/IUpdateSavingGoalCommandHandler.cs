using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Commands;

public interface IUpdateSavingGoalCommandHandler
{
    Task<SavingGoalDto> HandleAsync(UpdateSavingGoalCommand command, CancellationToken ct = default);
}
