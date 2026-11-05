using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class GroupFinanceRepository : IGroupFinanceRepository
{
    private readonly FinMateDbContext _context;

    public GroupFinanceRepository(FinMateDbContext context)
    {
        _context = context;
    }

    // Join với Groups để nhóm đã xóa mềm không còn bị tính hay cảnh báo. Danh mục nhóm được chọn thì nó quyết định, danh mục hệ thống không còn được xét.
    public Task<List<Budget>> GetMatchingBudgetsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default)
        => (from b in _context.Budgets.Include(b => b.Category).Include(b => b.GroupCategory)
            join g in _context.Groups on b.GroupId equals g.Id
            where b.GroupId == groupId
                && ((b.CategoryId == null && b.GroupCategoryId == null)
                    || (groupCategoryId != null && b.GroupCategoryId == groupCategoryId)
                    || (groupCategoryId == null && systemCategoryId != null && b.CategoryId == systemCategoryId))
            select b)
            .ToListAsync(ct);

    public Task<List<Budget>> ListBudgetsAsync(Guid groupId, CancellationToken ct = default)
        => _context.Budgets
            .Include(b => b.Category)
            .Include(b => b.GroupCategory)
            .Where(b => b.GroupId == groupId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(ct);

    public Task<Budget?> GetBudgetAsync(Guid groupId, Guid budgetId, CancellationToken ct = default)
        => _context.Budgets
            .Include(b => b.Category)
            .Include(b => b.GroupCategory)
            .FirstOrDefaultAsync(b => b.Id == budgetId && b.GroupId == groupId, ct);

    public Task<bool> BudgetExistsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default)
        => _context.Budgets.AnyAsync(b => b.GroupId == groupId && b.CategoryId == systemCategoryId && b.GroupCategoryId == groupCategoryId, ct);

    public Task<List<BudgetPeriod>> ListPeriodsAsync(Guid groupId, DateTimeOffset periodStart, CancellationToken ct = default)
        => _context.BudgetPeriods
            .Where(p => p.PeriodStart == periodStart && _context.Budgets.Any(b => b.Id == p.BudgetId && b.GroupId == groupId))
            .ToListAsync(ct);

    public void AddBudget(Budget budget) => _context.Budgets.Add(budget);

    public async Task<long> SumSharedSpendAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var query = _context.Transactions.Where(t =>
            t.GroupId == groupId
            && t.Status == TransactionStatus.Confirmed
            && t.TransactionType == TransactionType.Debit
            && (t.Category == null || t.Category.Slug != Category.LoanSlug)
            && t.TransactedAt >= from
            && t.TransactedAt < to);

        if (groupCategoryId is not null)
        {
            query = query.Where(t => t.GroupCategoryId == groupCategoryId);
        }
        else if (systemCategoryId is not null)
        {
            query = query.Where(t => t.GroupCategoryId == null && t.CategoryId == systemCategoryId);
        }

        return await query.SumAsync(t => (long?)t.AmountCents, ct) ?? 0;
    }

    // IgnoreQueryFilters trên Users: người đã yêu cầu xóa tài khoản vẫn còn giao dịch tới lúc xóa cứng.
    public async Task<List<SharedTransactionView>> ListSharedTransactionsAsync(Guid groupId, Guid? onlyUserId, DateTimeOffset start, DateTimeOffset end, int limit, CancellationToken ct = default)
    {
        var rows = await (from t in _context.Transactions.AsNoTracking().Include(x => x.Category).Include(x => x.GroupCategory)
                          join u in _context.Users.IgnoreQueryFilters() on t.UserId equals u.Id
                          where t.GroupId == groupId
                              && t.Status == TransactionStatus.Confirmed
                              && t.TransactedAt >= start
                              && t.TransactedAt < end
                              && (onlyUserId == null || t.UserId == onlyUserId)
                          orderby t.TransactedAt descending, t.Id
                          select new { Transaction = t, u.DisplayName, u.DeletedAt })
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(r => new SharedTransactionView(r.Transaction, r.DisplayName, r.DeletedAt is null)).ToList();
    }

    public Task<Transaction?> GetSharedTransactionAsync(Guid groupId, Guid transactionId, CancellationToken ct = default)
        => _context.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.GroupId == groupId, ct);

    public Task<List<GroupCategory>> ListCategoriesAsync(Guid groupId, CancellationToken ct = default)
        => _context.GroupCategories.Where(c => c.GroupId == groupId && c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);

    public Task<GroupCategory?> GetCategoryAsync(Guid groupId, Guid categoryId, CancellationToken ct = default)
        => _context.GroupCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.GroupId == groupId && c.IsActive, ct);

    public Task<bool> CategorySlugExistsAsync(Guid groupId, string slug, CancellationToken ct = default)
        => _context.GroupCategories.AnyAsync(c => c.GroupId == groupId && c.Slug == slug, ct);

    public Task<bool> CategoryHasActiveBudgetAsync(Guid categoryId, CancellationToken ct = default)
        => _context.Budgets.AnyAsync(b => b.GroupCategoryId == categoryId, ct);

    public void AddCategory(GroupCategory category) => _context.GroupCategories.Add(category);

    public Task<List<GroupShareRule>> ListActiveRulesForUserAsync(Guid userId, CancellationToken ct = default)
        => _context.GroupShareRules.Where(r => r.UserId == userId && r.IsActive).OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public Task<List<GroupShareRule>> ListRulesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
        => _context.GroupShareRules.Where(r => r.GroupId == groupId && r.UserId == userId && r.IsActive).OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public Task<GroupShareRule?> GetRuleAsync(Guid groupId, Guid userId, Guid ruleId, CancellationToken ct = default)
        => _context.GroupShareRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.GroupId == groupId && r.UserId == userId, ct);

    public void AddRule(GroupShareRule rule) => _context.GroupShareRules.Add(rule);

    public Task<List<SavingGoal>> ListGoalsAsync(Guid groupId, CancellationToken ct = default)
        => _context.SavingGoals.Where(g => g.GroupId == groupId).OrderByDescending(g => g.CreatedAt).ToListAsync(ct);

    public Task<SavingGoal?> GetGoalAsync(Guid groupId, Guid goalId, CancellationToken ct = default)
        => _context.SavingGoals.FirstOrDefaultAsync(g => g.Id == goalId && g.GroupId == groupId, ct);

    public async Task<List<GroupContributionView>> ListContributionsAsync(Guid goalId, int limit, CancellationToken ct = default)
    {
        var rows = await (from c in _context.GoalContributions.AsNoTracking()
                          join u in _context.Users.IgnoreQueryFilters() on c.UserId equals u.Id
                          where c.SavingGoalId == goalId
                          orderby c.ContributedAt descending
                          select new { Contribution = c, u.DisplayName, u.DeletedAt })
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(r => new GroupContributionView(r.Contribution, r.DisplayName, r.DeletedAt is null)).ToList();
    }

    public void AddGoal(SavingGoal goal) => _context.SavingGoals.Add(goal);

    public void AddContribution(GoalContribution contribution) => _context.GoalContributions.Add(contribution);
}
