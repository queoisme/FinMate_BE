using FinMate.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class UserHardDeleter : IUserHardDeleter
{
    private readonly FinMateDbContext _context;

    public UserHardDeleter(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task HardDeleteAsync(Guid userId, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: the soft-delete filter would otherwise hide the very row we need to remove.
        await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteDeleteAsync(ct);
    }
}
