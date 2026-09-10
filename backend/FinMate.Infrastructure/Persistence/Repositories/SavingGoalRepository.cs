using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SavingGoalRepository : ISavingGoalRepository
{
    private readonly FinMateDbContext _context;

    public SavingGoalRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<SavingGoal?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.SavingGoals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);

    public Task<List<SavingGoal>> GetListForUserAsync(Guid userId, SavingGoalStatus? status, CancellationToken ct = default)
    {
        var query = _context.SavingGoals.Where(g => g.UserId == userId);

        if (status is not null)
        {
            query = query.Where(g => g.Status == status);
        }

        return query.OrderByDescending(g => g.CreatedAt).ToListAsync(ct);
    }

    public async Task AddAsync(SavingGoal goal, CancellationToken ct = default)
    {
        _context.SavingGoals.Add(goal);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SavingGoal goal, CancellationToken ct = default)
    {
        _context.SavingGoals.Update(goal);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddContributionAsync(GoalContribution contribution, CancellationToken ct = default)
    {
        _context.GoalContributions.Add(contribution);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<GoalContribution>> GetContributionsAsync(Guid goalId, Guid userId, int limit, CancellationToken ct = default)
        => _context.GoalContributions
            .Where(c => c.SavingGoalId == goalId && c.UserId == userId)
            .OrderByDescending(c => c.ContributedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<List<SavingGoal>> GetOverdueUnnotifiedGoalsAsync(DateTimeOffset now, CancellationToken ct = default)
        => _context.SavingGoals
            .Where(g => g.Status == SavingGoalStatus.Active
                && g.Deadline != null
                && g.Deadline < now
                && g.DeadlineNotifiedAt == null)
            .ToListAsync(ct);
}
