using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface ICategoryRepository
{
    Task<Category?> GetOwnedByUserAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Category?> GetSystemBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<Category>> GetListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsBySlugAsync(Guid? userId, string slug, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
    Task UpdateAsync(Category category, CancellationToken ct = default);
}
