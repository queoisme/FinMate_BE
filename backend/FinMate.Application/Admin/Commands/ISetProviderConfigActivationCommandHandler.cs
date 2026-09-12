using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ISetProviderConfigActivationCommandHandler
{
    Task<ProviderConfigDto> HandleAsync(
        SetProviderConfigActivationCommand command, CancellationToken ct = default);
}
