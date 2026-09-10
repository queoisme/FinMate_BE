using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

public interface IGetAccountBalanceQueryHandler
{
    Task<AccountBalanceDto> HandleAsync(GetAccountBalanceQuery query, CancellationToken ct = default);
}
