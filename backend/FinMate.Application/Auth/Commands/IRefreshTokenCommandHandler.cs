using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IRefreshTokenCommandHandler
{
    Task<AuthResultDto> HandleAsync(RefreshTokenCommand command, CancellationToken ct = default);
}
