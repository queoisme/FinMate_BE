using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface ICategoryRepository
{
    Task<Category?> GetOwnedByUserAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Category?> GetSystemBySlugAsync(string slug, CancellationToken ct = default);
    /// <summary>Danh mục của người dùng. <paramref name="includeHidden"/> false (mặc định ở API) bỏ các danh mục hệ thống người dùng đã ẩn.</summary>
    Task<List<Category>> GetListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<Category>> GetListForUserAsync(Guid userId, bool includeHidden, CancellationToken ct = default);
    Task<HashSet<Guid>> GetHiddenIdsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsHiddenAsync(Guid userId, Guid categoryId, CancellationToken ct = default);
    Task SetHiddenAsync(Guid userId, Guid categoryId, bool hidden, CancellationToken ct = default);

    /// <summary>Danh mục hệ thống cho màn hình quản trị. <paramref name="includeInactive"/>
    /// false = chỉ danh mục còn cho chọn.</summary>
    Task<List<Category>> GetSystemListAsync(bool includeInactive, CancellationToken ct = default);
    Task<bool> ExistsBySlugAsync(Guid? userId, string slug, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
    Task UpdateAsync(Category category, CancellationToken ct = default);
}
