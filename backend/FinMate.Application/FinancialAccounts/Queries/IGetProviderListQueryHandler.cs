using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

public interface IGetProviderListQueryHandler
{
    Task<IReadOnlyList<ProviderOptionDto>> HandleAsync(
        GetProviderListQuery query, CancellationToken ct = default);
}
