using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class UserMethodRepository : IUserMethodRepository
{
    private readonly FinMateDbContext _context;

    public UserMethodRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<UserMethod>> GetActiveAsync(Guid userId, CancellationToken ct = default)
        => _context.UserMethods.Where(m => m.UserId == userId && m.Status == UserMethodStatus.Active).ToListAsync(ct);

    public Task<UserMethod?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.UserMethods.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct);

    public async Task UpdateAsync(UserMethod userMethod, CancellationToken ct = default)
    {
        _context.UserMethods.Update(userMethod);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ReplaceActiveAsync(UserMethod? ended, UserMethod added, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        if (ended is not null)
        {
            _context.UserMethods.Update(ended);
            await _context.SaveChangesAsync(ct);
        }

        _context.UserMethods.Add(added);
        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<MethodRecommendation?> GetRecommendationAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.MethodRecommendations.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

    public Task<MethodRecommendation?> GetLatestRecommendationAsync(Guid userId, CancellationToken ct = default)
        => _context.MethodRecommendations
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddRecommendationAsync(MethodRecommendation recommendation, CancellationToken ct = default)
    {
        _context.MethodRecommendations.Add(recommendation);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateRecommendationAsync(MethodRecommendation recommendation, CancellationToken ct = default)
    {
        _context.MethodRecommendations.Update(recommendation);
        await _context.SaveChangesAsync(ct);
    }

    public Task DeleteRecommendationsAsync(Guid userId, CancellationToken ct = default)
        => _context.MethodRecommendations.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
}
