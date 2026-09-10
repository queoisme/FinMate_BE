using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Commands;

public interface IToggleAccountMonitoringCommandHandler
{
    Task<FinancialAccountDto> HandleAsync(ToggleAccountMonitoringCommand command, CancellationToken ct = default);
}
