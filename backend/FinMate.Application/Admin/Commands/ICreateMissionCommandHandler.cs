using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ICreateMissionCommandHandler
{
    Task<AdminMissionDto> HandleAsync(CreateMissionCommand command, CancellationToken ct = default);
}
