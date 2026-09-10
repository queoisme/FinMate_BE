using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals.Commands;

public interface ICreateSavingGoalCommandHandler
{
    Task<SavingGoalDto> HandleAsync(CreateSavingGoalCommand command, CancellationToken ct = default);
}
