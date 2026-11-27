using FinMate.Application.Admin;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class OpsNotificationRepository : IOpsNotificationRepository
{
    private readonly FinMateDbContext _context;

    public OpsNotificationRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Guid>> ListActiveUserIdsByRolesAsync(IReadOnlyCollection<UserRole> roles, CancellationToken ct = default)
        => await _context.Users.AsNoTracking()
            .Where(u => !u.IsLocked && u.DeletedAt == null && roles.Contains(u.RoleAssignment!.RoleId))
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<Guid>> ListUsersWithUnreadAsync(
        OpsNotificationType type, Guid? relatedEntityId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
        => (await _context.OpsNotifications.AsNoTracking()
            .Where(n => n.ReadAt == null && n.Type == type && n.RelatedEntityId == relatedEntityId && userIds.Contains(n.UserId))
            .Select(n => n.UserId)
            .ToListAsync(ct)).ToHashSet();

    public async Task AddRangeAsync(IReadOnlyList<OpsNotification> notifications, CancellationToken ct = default)
    {
        _context.OpsNotifications.AddRange(notifications);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<OpsNotification> Items, int Total)> ListAsync(
        Guid userId, bool unreadOnly, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _context.OpsNotifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{AdminPaging.EscapeLike(search.Trim())}%";
            query = query.Where(n => EF.Functions.ILike(n.Title, pattern, "\\") || EF.Functions.ILike(n.Body, pattern, "\\"));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => _context.OpsNotifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    public async Task<bool> MarkReadAsync(Guid userId, Guid id, DateTimeOffset at, CancellationToken ct = default)
    {
        var row = await _context.OpsNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (row is null)
        {
            return false;
        }

        row.ReadAt ??= at;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default)
        => _context.OpsNotifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, at), ct);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => _context.OpsNotifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
}
