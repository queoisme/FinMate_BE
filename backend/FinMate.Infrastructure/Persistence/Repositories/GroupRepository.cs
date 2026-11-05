using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupRepository : IGroupRepository
{
    private const string UniqueViolation = "23505";

    private readonly FinMateDbContext _context;

    public GroupRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<Group?> GetByIdAsync(Guid groupId, CancellationToken ct = default)
        => _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);

    public Task<GroupMember?> GetActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.LeftAt == null, ct);

    public Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);

    public async Task<List<GroupWithMembership>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await (from m in _context.GroupMembers
                          join g in _context.Groups on m.GroupId equals g.Id
                          where m.UserId == userId && m.LeftAt == null
                          orderby g.CreatedAt
                          select new
                          {
                              Group = g,
                              Member = m,
                              Count = _context.GroupMembers.Count(o => o.GroupId == g.Id && o.LeftAt == null),
                          })
            .ToListAsync(ct);
        return rows.Select(r => new GroupWithMembership(r.Group, r.Member, r.Count)).ToList();
    }

    // IgnoreQueryFilters: người đã yêu cầu xóa tài khoản vẫn là thành viên tới lúc xóa cứng (admin có thể hủy yêu cầu), nên phải còn hiện ra.
    public async Task<List<GroupMemberView>> ListActiveMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await (from m in _context.GroupMembers
                          join u in _context.Users.IgnoreQueryFilters() on m.UserId equals u.Id
                          where m.GroupId == groupId && m.LeftAt == null
                          orderby m.JoinedAt
                          select new { Member = m, u.DisplayName, u.DeletedAt })
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Member.Role).Select(r => new GroupMemberView(r.Member, r.DisplayName, r.DeletedAt is null)).ToList();
    }

    public Task<List<Guid>> ListOwnedGroupIdsAsync(Guid userId, CancellationToken ct = default)
        => (from m in _context.GroupMembers
            join g in _context.Groups on m.GroupId equals g.Id
            where m.UserId == userId && m.LeftAt == null && m.Role == Domain.Enums.GroupRole.Owner
            select g.Id).ToListAsync(ct);

    public Task<int> CountActiveMembersAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupMembers.CountAsync(m => m.GroupId == groupId && m.LeftAt == null, ct);

    public Task<int> CountGroupsForUserAsync(Guid userId, CancellationToken ct = default)
        => (from m in _context.GroupMembers
            join g in _context.Groups on m.GroupId equals g.Id
            where m.UserId == userId && m.LeftAt == null
            select m.Id).CountAsync(ct);

    public void AddGroup(Group group) => _context.Groups.Add(group);

    public void AddMember(GroupMember member) => _context.GroupMembers.Add(member);

    public void AddActivity(GroupActivity activity) => _context.GroupActivities.Add(activity);

    public void AddInvitation(GroupInvitation invitation) => _context.GroupInvitations.Add(invitation);

    public Task<GroupInvitation?> GetInvitationAsync(Guid groupId, Guid invitationId, CancellationToken ct = default)
        => _context.GroupInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.GroupId == groupId, ct);

    public Task<GroupInvitation?> GetInvitationByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => _context.GroupInvitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public Task<List<GroupInvitation>> ListPendingInvitationsAsync(Guid groupId, DateTimeOffset now, CancellationToken ct = default)
        => PendingInvitations(groupId, now).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);

    public Task<int> CountPendingInvitationsAsync(Guid groupId, DateTimeOffset now, CancellationToken ct = default)
        => PendingInvitations(groupId, now).CountAsync(ct);

    public Task<List<GroupActivity>> ListActivitiesAsync(Guid groupId, int limit, CancellationToken ct = default)
        => _context.GroupActivities
            .AsNoTracking()
            .Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task TransferOwnershipAsync(GroupMember currentOwner, GroupMember newOwner, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        currentOwner.Role = Domain.Enums.GroupRole.Manager;
        currentOwner.UpdatedAt = now;
        await SaveChangesAsync(ct);

        newOwner.Role = Domain.Enums.GroupRole.Owner;
        newOwner.PermissionsGranted = Domain.Enums.GroupPermission.None;
        newOwner.PermissionsRevoked = Domain.Enums.GroupPermission.None;
        newOwner.UpdatedAt = now;
        await SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: var name } && name!.StartsWith("uq_group_members", StringComparison.Ordinal))
        {
            throw new ConflictException(GroupErrorCodes.MembershipConflict, "Thành viên của nhóm vừa được cập nhật ở nơi khác. Vui lòng thử lại.");
        }
    }

    private IQueryable<GroupInvitation> PendingInvitations(Guid groupId, DateTimeOffset now)
        => _context.GroupInvitations.Where(i => i.GroupId == groupId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now);
}
