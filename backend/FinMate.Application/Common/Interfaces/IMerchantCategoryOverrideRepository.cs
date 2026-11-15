using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IMerchantCategoryOverrideRepository
{
    Task<MerchantCategoryOverride?> GetAsync(Guid userId, string merchantKey, CancellationToken ct = default);
    Task AddAsync(MerchantCategoryOverride entity, CancellationToken ct = default);
    Task UpdateAsync(MerchantCategoryOverride entity, CancellationToken ct = default);
}
