using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly FinMateDbContext _context;

    public UserRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default)
        => _context.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
        => _context.Users.AnyAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<UserListResult> GetPageAsync(
        UserListFilter filter, CancellationToken ct = default)
    {
        // IgnoreQueryFilters vì User có global filter deleted_at IS NULL: admin cần thấy được
        // tài khoản đang chờ xoá cứng để đối chiếu với data_deletion_requests.
        var query = filter.IncludeDeleted
            ? _context.Users.IgnoreQueryFilters().AsNoTracking()
            : _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.Email, $"%{search}%")
                || EF.Functions.ILike(u.DisplayName, $"%{search}%"));
        }

        if (filter.Role is { } role)
        {
            query = query.Where(u => u.Role == role);
        }

        if (filter.IsLocked is { } isLocked)
        {
            query = query.Where(u => u.IsLocked == isLocked);
        }

        if (KeysetCursor.TryDecode(filter.Cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(u =>
                u.CreatedAt < cursorAt || (u.CreatedAt == cursorAt && u.Id.CompareTo(cursorId) < 0));
        }

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Take(filter.Limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > filter.Limit)
        {
            var last = items[filter.Limit - 1];
            nextCursor = KeysetCursor.Encode(last.CreatedAt, last.Id);
            items = items.Take(filter.Limit).ToList();
        }

        return new UserListResult(items, nextCursor);
    }
}
