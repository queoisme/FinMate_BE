using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class MissionRepository : IMissionRepository
{
    private readonly FinMateDbContext _context;

    public MissionRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<Mission>> GetActiveMissionsAsync(CancellationToken ct = default)
        => _context.Missions.Where(m => m.IsActive).OrderBy(m => m.PeriodType).ThenBy(m => m.Code).ToListAsync(ct);

    public Task<List<Mission>> GetActiveMissionsByConditionAsync(MissionConditionType conditionType, CancellationToken ct = default)
        => _context.Missions.Where(m => m.IsActive && m.ConditionType == conditionType).ToListAsync(ct);

    public Task<Mission?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _context.Missions.FirstOrDefaultAsync(m => m.Code == code, ct);

    public Task<List<UserMission>> GetUserMissionsAsync(
        Guid userId, IReadOnlyCollection<Guid> missionIds, DateOnly periodStart, CancellationToken ct = default)
        => _context.UserMissions
            .Where(m => m.UserId == userId && missionIds.Contains(m.MissionId) && m.PeriodStart == periodStart)
            .ToListAsync(ct);

    public Task<List<UserMission>> GetActiveUserMissionsAsync(Guid userId, DateOnly today, CancellationToken ct = default)
        => _context.UserMissions
            .Include(m => m.Mission)
            .Where(m => m.UserId == userId && m.PeriodStart <= today && m.PeriodEnd >= today)
            .ToListAsync(ct);

    public Task<List<UserMission>> GetCompletedUserMissionsAsync(Guid userId, int limit, CancellationToken ct = default)
        => _context.UserMissions
            .Include(m => m.Mission)
            .Where(m => m.UserId == userId && m.IsCompleted)
            .OrderByDescending(m => m.CompletedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<bool> HasEverCompletedAsync(Guid userId, string code, CancellationToken ct = default)
        => _context.UserMissions
            .AnyAsync(m => m.UserId == userId && m.IsCompleted && m.Mission!.Code == code, ct);

    public void AddUserMission(UserMission userMission)
        => _context.UserMissions.Add(userMission);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);
}
