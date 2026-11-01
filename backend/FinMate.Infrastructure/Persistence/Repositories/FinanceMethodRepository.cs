using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class FinanceMethodRepository : IFinanceMethodRepository
{
    private readonly FinMateDbContext _context;

    public FinanceMethodRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<FinanceMethod>> GetMethodsAsync(bool includeInactive, CancellationToken ct = default)
        => _context.FinanceMethods
            .AsNoTracking()
            .Where(m => includeInactive || m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(ct);

    public Task<List<FinanceMethod>> GetMethodsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return _context.FinanceMethods.AsNoTracking().Where(m => idList.Contains(m.Id)).ToListAsync(ct);
    }

    public Task<Dictionary<Guid, int>> CountActiveUsersAsync(CancellationToken ct = default)
        => _context.UserMethods
            .AsNoTracking()
            .Where(m => m.Status == UserMethodStatus.Active)
            .GroupBy(m => m.MethodId)
            .Select(g => new { MethodId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MethodId, x => x.Count, ct);

    public Task<FinanceMethod?> GetMethodByIdAsync(Guid id, CancellationToken ct = default)
        => _context.FinanceMethods.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<FinanceMethod?> GetMethodByCodeAsync(string code, CancellationToken ct = default)
        => _context.FinanceMethods.FirstOrDefaultAsync(m => m.Code == code, ct);

    public async Task AddMethodAsync(FinanceMethod method, CancellationToken ct = default)
    {
        _context.FinanceMethods.Add(method);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateMethodAsync(FinanceMethod method, CancellationToken ct = default)
    {
        _context.FinanceMethods.Update(method);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<GoalTemplate>> GetTemplatesAsync(bool includeInactive, CancellationToken ct = default)
        => _context.GoalTemplates
            .AsNoTracking()
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Code)
            .ToListAsync(ct);

    public Task<GoalTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default)
        => _context.GoalTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<GoalTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default)
        => _context.GoalTemplates.FirstOrDefaultAsync(t => t.Code == code, ct);

    public async Task AddTemplateAsync(GoalTemplate template, CancellationToken ct = default)
    {
        _context.GoalTemplates.Add(template);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateTemplateAsync(GoalTemplate template, CancellationToken ct = default)
    {
        _context.GoalTemplates.Update(template);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<MethodGoalTemplate>> GetLinksForMethodsAsync(IEnumerable<Guid> methodIds, CancellationToken ct = default)
    {
        var ids = methodIds.ToList();
        return _context.MethodGoalTemplates.AsNoTracking().Where(l => ids.Contains(l.MethodId)).ToListAsync(ct);
    }

    public async Task ReplaceLinksAsync(Guid methodId, IReadOnlyList<MethodGoalTemplate> links, CancellationToken ct = default)
    {
        // Gộp thay vì xóa-rồi-thêm: xóa một dòng rồi thêm lại dòng cùng khóa (methodId, templateId) trong cùng một DbContext làm EF báo trùng khóa đang theo dõi.
        var existing = await _context.MethodGoalTemplates.Where(l => l.MethodId == methodId).ToListAsync(ct);
        var wanted = links.ToDictionary(l => l.TemplateId);

        foreach (var link in existing)
        {
            if (wanted.Remove(link.TemplateId, out var replacement))
            {
                link.Conditions = replacement.Conditions;
                link.SortOrder = replacement.SortOrder;
            }
            else
            {
                _context.MethodGoalTemplates.Remove(link);
            }
        }

        _context.MethodGoalTemplates.AddRange(wanted.Values);
        await _context.SaveChangesAsync(ct);   // một lần lưu = một giao dịch
    }
}
