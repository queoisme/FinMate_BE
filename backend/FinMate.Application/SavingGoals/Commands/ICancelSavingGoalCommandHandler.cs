using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Commands;

public interface ICancelSavingGoalCommandHandler
{
    Task<SavingGoalDto> HandleAsync(CancelSavingGoalCommand command, CancellationToken ct = default);
}
