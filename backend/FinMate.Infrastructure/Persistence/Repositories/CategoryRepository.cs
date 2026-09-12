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

    public Task<List<Category>> GetListForUserAsync(Guid userId, CancellationToken ct = default)
        => _context.Categories
            .Where(c => (c.UserId == null || c.UserId == userId) && c.IsActive)
            .ToListAsync(ct);

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
