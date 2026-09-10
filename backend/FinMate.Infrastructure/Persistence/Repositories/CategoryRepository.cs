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

    public Task<Category?> GetSystemBySlugAsync(string slug, CancellationToken ct = default)
        => _context.Categories.FirstOrDefaultAsync(c => c.UserId == null && c.Slug == slug, ct);

    public Task<List<Category>> GetListForUserAsync(Guid userId, CancellationToken ct = default)
        => _context.Categories
            .Where(c => c.UserId == null || c.UserId == userId)
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
