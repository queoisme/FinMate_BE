using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

public class GetProviderListQueryHandler : IGetProviderListQueryHandler
{
    private readonly IProviderConfigRepository _providerConfigRepository;

    public GetProviderListQueryHandler(IProviderConfigRepository providerConfigRepository)
    {
        _providerConfigRepository = providerConfigRepository;
    }

    public async Task<IReadOnlyList<ProviderOptionDto>> HandleAsync(
        GetProviderListQuery query, CancellationToken ct = default)
    {
        // Chỉ provider đang BẬT. Provider tắt là provider chưa đối chiếu được package_name
        // với thông báo thật — cho người dùng chọn nó là hứa một thứ sẽ không bao giờ chạy.
        var configs = await _providerConfigRepository.GetListAsync(includeInactive: false, ct);

        return configs
            .Select(c => new ProviderOptionDto(c.Id, c.ProviderKey, c.DisplayName, c.PackageName, c.AccountType))
            .ToList();
    }
}
