using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IKnowledgeReviewRepository
{
    /// <summary>Theo dõi để sửa.</summary>
    Task<KnowledgeReview?> GetAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, KnowledgeReview>> GetManyAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default);

    Task AddAsync(KnowledgeReview review, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Mốc backend bắt đầu ghi người soạn; null nếu chưa có (DB chưa chạy migration).</summary>
    Task<DateTimeOffset?> GetTrackedSinceAsync(CancellationToken ct = default);
}
