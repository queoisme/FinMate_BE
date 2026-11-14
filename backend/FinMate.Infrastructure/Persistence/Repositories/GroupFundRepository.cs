using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupFundRepository : IGroupFundRepository
{
    private readonly FinMateDbContext _context;

    public GroupFundRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<GroupFund>> ListAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupFunds
            .Where(f => f.GroupId == groupId)
            .OrderBy(f => f.ArchivedAt != null)
            .ThenBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public Task<GroupFund?> GetAsync(Guid groupId, Guid fundId, CancellationToken ct = default)
        => _context.GroupFunds.FirstOrDefaultAsync(f => f.Id == fundId && f.GroupId == groupId, ct);

    public Task<int> CountActiveAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupFunds.CountAsync(f => f.GroupId == groupId && f.ArchivedAt == null, ct);

    public void Add(GroupFund fund) => _context.GroupFunds.Add(fund);
}
