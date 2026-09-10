using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IFinancialAccountRepository
{
    Task<FinancialAccount?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<List<FinancialAccount>> GetListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsByUserAndPackageNameAsync(Guid userId, string packageName, CancellationToken ct = default);
    Task<FinancialAccount?> GetByUserAndMonitoredPackageAsync(Guid userId, string packageName, CancellationToken ct = default);
    Task AddAsync(FinancialAccount account, CancellationToken ct = default);
    Task UpdateAsync(FinancialAccount account, CancellationToken ct = default);
}
