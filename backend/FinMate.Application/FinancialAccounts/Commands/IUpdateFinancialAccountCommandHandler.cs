using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Commands;

public interface IUpdateFinancialAccountCommandHandler
{
    Task<FinancialAccountDto> HandleAsync(UpdateFinancialAccountCommand command, CancellationToken ct = default);
}
