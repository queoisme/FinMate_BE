using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetProviderConfigListQueryHandler
{
    Task<IReadOnlyList<ProviderConfigDto>> HandleAsync(
        GetProviderConfigListQuery query, CancellationToken ct = default);
}
