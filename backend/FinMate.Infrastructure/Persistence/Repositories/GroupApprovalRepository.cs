using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupApprovalRepository : IGroupApprovalRepository
{
    private readonly FinMateDbContext _context;

    public GroupApprovalRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public void Add(GroupApproval approval) => _context.GroupApprovals.Add(approval);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public Task<GroupApproval?> GetAsync(Guid groupId, Guid approvalId, CancellationToken ct = default)
        => _context.GroupApprovals.FirstOrDefaultAsync(a => a.GroupId == groupId && a.Id == approvalId, ct);

    public Task<GroupApproval?> GetByClientRequestAsync(Guid groupId, Guid requestedByUserId, Guid clientRequestId, CancellationToken ct = default)
        => _context.GroupApprovals.FirstOrDefaultAsync(
            a => a.GroupId == groupId && a.RequestedByUserId == requestedByUserId && a.ClientRequestId == clientRequestId, ct);

    // IgnoreQueryFilters trên Users: người đã yêu cầu xóa tài khoản vẫn đứng tên yêu cầu tới lúc xóa cứng.
    public async Task<List<GroupApprovalView>> ListAsync(Guid groupId, GroupApprovalStatus? status, Guid? onlyUserId, int limit, CancellationToken ct = default)
    {
        var rows = await _context.GroupApprovals.AsNoTracking()
            .Where(a => a.GroupId == groupId
                && (status == null || a.Status == status)
                && (onlyUserId == null || a.RequestedByUserId == onlyUserId))
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(ct);
        var ids = rows.Where(r => r.RequestedByUserId != null).Select(r => r.RequestedByUserId!.Value).Distinct().ToList();
        var names = await _context.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(r => new GroupApprovalView(r, r.RequestedByUserId is { } id && names.TryGetValue(id, out var n) ? n : null)).ToList();
    }

    public Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken ct = default)
        => _context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => (string?)u.DisplayName).FirstOrDefaultAsync(ct);
}
