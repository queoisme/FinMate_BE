using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class BudgetRepository : IBudgetRepository
{
    private readonly FinMateDbContext _context;

    public BudgetRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);

    public Task<List<Budget>> GetListForUserAsync(Guid userId, CancellationToken ct = default)
        => _context.Budgets
            .Include(b => b.Category)
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.CategoryId == null ? 0 : 1)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync(ct);

    // Include(Category) vì BudgetPeriodService dùng tên danh mục làm "scope" trong nội dung
    // cảnh báo tức thì. Thiếu nó thì Category là null và MỌI cảnh báo đều ghi "toàn bộ chi
    // tiêu" — kể cả cảnh báo của budget Ăn uống. Không lỗi, chỉ là thông điệp sai.
    public Task<List<Budget>> GetMatchingBudgetsAsync(Guid userId, Guid? categoryId, CancellationToken ct = default)
        => _context.Budgets
            .Include(b => b.Category)
            .Where(b => b.UserId == userId
                && (b.CategoryId == null || (categoryId != null && b.CategoryId == categoryId)))
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid userId, Guid? categoryId, BudgetPeriodType periodType, CancellationToken ct = default)
        => _context.Budgets.AnyAsync(
            b => b.UserId == userId && b.CategoryId == categoryId && b.PeriodType == periodType, ct);

    public async Task AddAsync(Budget budget, CancellationToken ct = default)
    {
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        _context.Budgets.Update(budget);
        await _context.SaveChangesAsync(ct);
    }

    public Task<BudgetPeriod?> GetPeriodAsync(Guid budgetId, DateTimeOffset periodStart, CancellationToken ct = default)
        => _context.BudgetPeriods
            .FirstOrDefaultAsync(p => p.BudgetId == budgetId && p.PeriodStart == periodStart, ct);

    public Task<List<BudgetPeriod>> GetPeriodsForUserAsync(Guid userId, DateTimeOffset periodStart, CancellationToken ct = default)
        => _context.BudgetPeriods
            .Where(p => p.PeriodStart == periodStart
                && _context.Budgets.Any(b => b.Id == p.BudgetId && b.UserId == userId))
            .ToListAsync(ct);

    public void AddPeriod(BudgetPeriod period)
        => _context.BudgetPeriods.Add(period);

    // Join tường minh qua _context.Budgets (đã có query filter soft-delete) thay vì Include:
    // budget bị xóa mềm không được sinh cảnh báo, và join cho ta luôn UserId — BudgetPeriod
    // không giữ user_id.
    public async Task<List<BudgetAlertCandidate>> GetPeriodsForAlertAsync(DateTimeOffset now, CancellationToken ct = default)
        => await (from p in _context.BudgetPeriods
                  join b in _context.Budgets.Include(b => b.Category) on p.BudgetId equals b.Id
                  where p.PeriodStart <= now
                      && p.PeriodEnd > now
                      && p.SpentCents * 100 >= p.LimitCents * 70
                      && (p.Alert70SentAt == null || p.Alert90SentAt == null || p.Alert100SentAt == null)
                  select new BudgetAlertCandidate(p, b))
            .ToListAsync(ct);

    public async Task UpdatePeriodAsync(BudgetPeriod period, CancellationToken ct = default)
    {
        _context.BudgetPeriods.Update(period);
        await _context.SaveChangesAsync(ct);
    }
}
