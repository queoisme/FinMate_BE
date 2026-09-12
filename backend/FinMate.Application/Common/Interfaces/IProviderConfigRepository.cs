using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IProviderConfigRepository
{
    Task<ProviderConfig?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<ProviderConfig>> GetListAsync(bool includeInactive, CancellationToken ct = default);

    Task<bool> ExistsByProviderKeyAsync(string providerKey, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="excludeId"/> để lệnh sửa không tự coi chính nó là bản trùng.
    /// </summary>
    Task<bool> ExistsByPackageNameAsync(string packageName, Guid? excludeId, CancellationToken ct = default);

    Task AddAsync(ProviderConfig config, CancellationToken ct = default);
    Task UpdateAsync(ProviderConfig config, CancellationToken ct = default);
}
