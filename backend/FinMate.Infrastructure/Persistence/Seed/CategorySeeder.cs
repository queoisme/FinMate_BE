using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

public static class CategorySeeder
{
    // Slug phải khớp đúng taxonomy category_slug mà AI Service trả về (ARCHITECTURE.md §3.3).
    private static readonly (string Slug, string Name, string IconName)[] Categories =
    {
        ("food", "Ăn uống", "restaurant"),
        ("transport", "Di chuyển", "directions_car"),
        ("shopping", "Mua sắm", "shopping_bag"),
        ("education", "Giáo dục", "school"),
        ("housing", "Nhà ở", "home"),
        ("bills", "Hóa đơn", "receipt"),
        ("entertainment", "Giải trí", "movie"),
        ("health", "Sức khỏe", "favorite"),
        ("family", "Gia đình", "family_restroom"),
        ("income", "Thu nhập", "payments"),
        ("other", "Khác", "category"),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existingSlugs = await context.Categories
            .Where(c => c.UserId == null)
            .Select(c => c.Slug)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var category in Categories)
        {
            if (existingSlugs.Contains(category.Slug))
            {
                continue;
            }

            context.Categories.Add(new Category
            {
                UserId = null,
                Name = category.Name,
                Slug = category.Slug,
                IconName = category.IconName,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(ct);
    }
}
