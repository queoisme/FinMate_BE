using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Commands;

public interface ICreateFinancialAccountCommandHandler
{
    Task<FinancialAccountDto> HandleAsync(CreateFinancialAccountCommand command, CancellationToken ct = default);
}
