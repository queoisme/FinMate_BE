using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class KnowledgeReviewRepository(FinMateDbContext context) : IKnowledgeReviewRepository
{
    public Task<KnowledgeReview?> GetAsync(Guid documentId, CancellationToken ct = default)
        => context.KnowledgeReviews.FirstOrDefaultAsync(r => r.DocumentId == documentId, ct);

    public async Task<IReadOnlyDictionary<Guid, KnowledgeReview>> GetManyAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => documentIds.Count == 0
            ? new Dictionary<Guid, KnowledgeReview>()
            : await context.KnowledgeReviews.AsNoTracking().Where(r => documentIds.Contains(r.DocumentId)).ToDictionaryAsync(r => r.DocumentId, ct);

    public async Task AddAsync(KnowledgeReview review, CancellationToken ct = default)
    {
        context.KnowledgeReviews.Add(review);
        await context.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

    public async Task<DateTimeOffset?> GetTrackedSinceAsync(CancellationToken ct = default)
        => (await context.KnowledgeReviewCutovers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == 1, ct))?.TrackedSince;
}
