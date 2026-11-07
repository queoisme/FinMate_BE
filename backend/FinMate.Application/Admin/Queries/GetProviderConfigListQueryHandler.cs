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

    public async Task<PagedResult<ProviderConfigDto>> HandleAsync(
        GetProviderConfigListQuery query, CancellationToken ct = default)
    {
        var configs = await _providerConfigRepository.GetListAsync(query.IncludeInactive, ct);
        var matched = configs
            .Where(c => AdminPaging.Matches(query.Search, c.DisplayName, c.ProviderKey, c.PackageName))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id);
        return AdminPaging.Page(matched, query.Offset, query.Limit).Map(AdminMapper.ToDto);
    }
}
