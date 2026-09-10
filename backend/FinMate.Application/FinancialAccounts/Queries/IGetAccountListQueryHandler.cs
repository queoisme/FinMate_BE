using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

public interface IGetAccountListQueryHandler
{
    Task<IReadOnlyList<FinancialAccountDto>> HandleAsync(GetAccountListQuery query, CancellationToken ct = default);
}
