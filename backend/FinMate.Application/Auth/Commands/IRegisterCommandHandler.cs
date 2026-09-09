using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IRegisterCommandHandler
{
    Task<UserProfileDto> HandleAsync(RegisterCommand command, CancellationToken ct = default);
}
