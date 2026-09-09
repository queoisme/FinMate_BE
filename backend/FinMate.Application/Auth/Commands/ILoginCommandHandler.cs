using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface ILoginCommandHandler
{
    Task<AuthResultDto> HandleAsync(LoginCommand command, CancellationToken ct = default);
}
