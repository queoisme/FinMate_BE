using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ICreateProviderConfigCommandHandler
{
    Task<ProviderConfigDto> HandleAsync(CreateProviderConfigCommand command, CancellationToken ct = default);
}
