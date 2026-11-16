using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly FinMateDbContext _context;

    public CategoryRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<Category?> GetOwnedByUserAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    // Chỉ trả danh mục còn BẬT. Đây là đường AnalyzeNotificationCommandHandler dùng để map
    // category_slug từ AI Service: admin đã tắt một danh mục thì giao dịch MỚI không được rơi
    // vào đó nữa — draft sẽ không có danh mục và người dùng tự chọn, đúng nghĩa "ngừng cho
    // chọn mới". Giao dịch cũ vẫn giữ nguyên category_id của chúng.
    public Task<Category?> GetSystemBySlugAsync(string slug, CancellationToken ct = default)
        => _context.Categories.FirstOrDefaultAsync(
            c => c.UserId == null && c.Slug == slug && c.IsActive, ct);

    // Mặc định (đường nội bộ: báo cáo, QR, dự báo) vẫn trả cả danh mục đã ẩn để tên của giao dịch cũ không mất.
    public Task<List<Category>> GetListForUserAsync(Guid userId, CancellationToken ct = default)
        => GetListForUserAsync(userId, includeHidden: true, ct);

    public Task<List<Category>> GetListForUserAsync(Guid userId, bool includeHidden, CancellationToken ct = default)
        => _context.Categories
            .Where(c => (c.UserId == null || c.UserId == userId) && c.IsActive
                && (includeHidden || !_context.UserHiddenCategories.Any(h => h.UserId == userId && h.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<HashSet<Guid>> GetHiddenIdsAsync(Guid userId, CancellationToken ct = default)
        => (await _context.UserHiddenCategories.Where(h => h.UserId == userId).Select(h => h.CategoryId).ToListAsync(ct)).ToHashSet();

    public Task<bool> IsHiddenAsync(Guid userId, Guid categoryId, CancellationToken ct = default)
        => _context.UserHiddenCategories.AnyAsync(h => h.UserId == userId && h.CategoryId == categoryId, ct);

    public async Task SetHiddenAsync(Guid userId, Guid categoryId, bool hidden, CancellationToken ct = default)
    {
        var existing = await _context.UserHiddenCategories.FirstOrDefaultAsync(h => h.UserId == userId && h.CategoryId == categoryId, ct);
        if (hidden && existing is null)
        {
            _context.UserHiddenCategories.Add(new UserHiddenCategory { UserId = userId, CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow });
        }
        else if (!hidden && existing is not null)
        {
            _context.UserHiddenCategories.Remove(existing);
        }
        else
        {
            return;
        }

        await _context.SaveChangesAsync(ct);
    }

    public Task<List<Category>> GetSystemListAsync(bool includeInactive, CancellationToken ct = default)
        => _context.Categories
            .Where(c => c.UserId == null && (includeInactive || c.IsActive))
            .OrderBy(c => c.Slug)
            .ToListAsync(ct);

    public Task<bool> ExistsBySlugAsync(Guid? userId, string slug, CancellationToken ct = default)
        => _context.Categories.AnyAsync(c => c.UserId == userId && c.Slug == slug, ct);

    public async Task AddAsync(Category category, CancellationToken ct = default)
    {
        _context.Categories.Add(category);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Category category, CancellationToken ct = default)
    {
        _context.Categories.Update(category);
        await _context.SaveChangesAsync(ct);
    }
}
