using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ISetMissionActivationCommandHandler
{
    Task<AdminMissionDto> HandleAsync(SetMissionActivationCommand command, CancellationToken ct = default);
}
