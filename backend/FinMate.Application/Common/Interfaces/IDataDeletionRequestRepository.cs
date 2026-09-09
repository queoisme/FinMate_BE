using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IDataDeletionRequestRepository
{
    Task AddAsync(DataDeletionRequest request, CancellationToken ct = default);
    Task<List<DataDeletionRequest>> GetDueAsync(DateTimeOffset asOf, CancellationToken ct = default);
    Task MarkProcessedAsync(DataDeletionRequest request, CancellationToken ct = default);
}
