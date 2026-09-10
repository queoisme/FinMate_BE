using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IGoogleLoginCommandHandler
{
    Task<AuthResultDto> HandleAsync(GoogleLoginCommand command, CancellationToken ct = default);
}
