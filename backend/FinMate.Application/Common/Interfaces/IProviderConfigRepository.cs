using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IProviderConfigRepository
{
    Task<ProviderConfig?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
