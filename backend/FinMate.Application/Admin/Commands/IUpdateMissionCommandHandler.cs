using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface IUpdateMissionCommandHandler
{
    Task<AdminMissionDto> HandleAsync(UpdateMissionCommand command, CancellationToken ct = default);
}
