using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface IUpdateProviderConfigCommandHandler
{
    Task<ProviderConfigDto> HandleAsync(UpdateProviderConfigCommand command, CancellationToken ct = default);
}
