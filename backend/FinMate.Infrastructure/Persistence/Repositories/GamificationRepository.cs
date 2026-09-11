using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities.Gamification;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GamificationRepository : IGamificationRepository
{
    private readonly FinMateDbContext _context;

    public GamificationRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<UserGamification?> GetProfileAsync(Guid userId, CancellationToken ct = default)
        => _context.UserGamifications.FirstOrDefaultAsync(g => g.UserId == userId, ct);

    public void AddProfile(UserGamification profile)
        => _context.UserGamifications.Add(profile);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public Task<List<UserGamification>> GetProfilesWithStaleStreakAsync(DateOnly today, CancellationToken ct = default)
        => _context.UserGamifications
            .Where(g => g.CurrentStreakDays > 0 && (g.LastActivityDate == null || g.LastActivityDate < today))
            .ToListAsync(ct);

    public Task<List<MascotItem>> GetAllMascotItemsAsync(CancellationToken ct = default)
        => _context.MascotItems.OrderBy(i => i.ItemType).ThenBy(i => i.UnlockLevel).ToListAsync(ct);

    public Task<List<UserMascotItem>> GetOwnedMascotItemsAsync(Guid userId, CancellationToken ct = default)
        => _context.UserMascotItems
            .Include(i => i.MascotItem)
            .Where(i => i.UserId == userId)
            .ToListAsync(ct);

    public void AddOwnedMascotItem(UserMascotItem item)
        => _context.UserMascotItems.Add(item);
}
