using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetProviderConfigListQueryHandler : IGetProviderConfigListQueryHandler
{
    private readonly IProviderConfigRepository _providerConfigRepository;

    public GetProviderConfigListQueryHandler(IProviderConfigRepository providerConfigRepository)
    {
        _providerConfigRepository = providerConfigRepository;
    }

    public async Task<IReadOnlyList<ProviderConfigDto>> HandleAsync(
        GetProviderConfigListQuery query, CancellationToken ct = default)
    {
        var configs = await _providerConfigRepository.GetListAsync(query.IncludeInactive, ct);
        return configs.Select(AdminMapper.ToDto).ToList();
    }
}
