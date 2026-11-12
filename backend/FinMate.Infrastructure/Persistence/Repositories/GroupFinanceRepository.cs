using System.Linq.Expressions;
using FinMate.Application.Budgets;
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
        => Math.Max(0, await SumSharedNetSpendAsync(BudgetSpendScope.Group(groupId, systemCategoryId, groupCategoryId, from, to), [], ct));

    public async Task<long> SumSharedNetSpendAsync(Expression<Func<Transaction, bool>> scope, IReadOnlyCollection<Guid> excludeTransactionIds, CancellationToken ct = default)
    {
        var query = _context.Transactions.Where(scope).Where(BudgetSpendScope.Counts);
        if (excludeTransactionIds.Count > 0)
        {
            query = query.Where(t => !excludeTransactionIds.Contains(t.Id));
        }

        return await query.Select(BudgetSpendScope.Signed).SumAsync(ct);
    }

    // IgnoreQueryFilters trên Users: người đã yêu cầu xóa tài khoản vẫn còn giao dịch tới lúc xóa cứng.
    public async Task<SharedTransactionPage> ListSharedTransactionsAsync(Guid groupId, Guid? onlyUserId, DateTimeOffset start, DateTimeOffset end, string? cursor, int limit, CancellationToken ct = default)
    {
        var query = from t in _context.Transactions.AsNoTracking().Include(x => x.Category).Include(x => x.GroupCategory)
                    join u in _context.Users.IgnoreQueryFilters() on t.UserId equals u.Id
                    where t.GroupId == groupId
                        && t.Status == TransactionStatus.Confirmed
                        && t.TransactedAt >= start
                        && t.TransactedAt < end
                        && (onlyUserId == null || t.UserId == onlyUserId)
                    select new { Transaction = t, u.DisplayName, u.DeletedAt };

        if (KeysetCursor.TryDecode(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(r =>
                r.Transaction.TransactedAt < cursorAt || (r.Transaction.TransactedAt == cursorAt && r.Transaction.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.Transaction.TransactedAt)
            .ThenByDescending(r => r.Transaction.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1].Transaction;
            nextCursor = KeysetCursor.Encode(last.TransactedAt, last.Id);
            rows = rows.Take(limit).ToList();
        }

        return new SharedTransactionPage(rows.Select(r => new SharedTransactionView(r.Transaction, r.DisplayName, r.DeletedAt is null)).ToList(), nextCursor);
    }

    public Task<List<GroupReportRow>> ListReportRowsAsync(Guid groupId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        => (from t in _context.Transactions
            join u in _context.Users.IgnoreQueryFilters() on t.UserId equals u.Id
            where t.GroupId == groupId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactedAt >= start
                && t.TransactedAt < end
                && (t.Category == null || t.Category.Slug != Category.LoanSlug)
            select new GroupReportRow(
                t.UserId,
                u.DisplayName,
                u.DeletedAt == null,
                t.TransactionType,
                t.AmountCents,
                t.GroupCategoryId,
                t.GroupCategory != null ? t.GroupCategory.Name : null,
                t.CategoryId,
                t.Category != null ? t.Category.Name : null,
                t.Category != null && t.Category.IsSystem))
            .ToListAsync(ct);

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

    public async Task<GroupContributionPage> ListContributionsAsync(Guid goalId, string? cursor, int limit, CancellationToken ct = default)
    {
        var query = from c in _context.GoalContributions.AsNoTracking()
                    join u in _context.Users.IgnoreQueryFilters() on c.UserId equals u.Id
                    where c.SavingGoalId == goalId
                    select new { Contribution = c, u.DisplayName, u.DeletedAt };

        if (KeysetCursor.TryDecode(cursor, out var cursorAt, out var cursorId))
        {
            query = query.Where(r =>
                r.Contribution.ContributedAt < cursorAt || (r.Contribution.ContributedAt == cursorAt && r.Contribution.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.Contribution.ContributedAt)
            .ThenByDescending(r => r.Contribution.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1].Contribution;
            nextCursor = KeysetCursor.Encode(last.ContributedAt, last.Id);
            rows = rows.Take(limit).ToList();
        }

        return new GroupContributionPage(rows.Select(r => new GroupContributionView(r.Contribution, r.DisplayName, r.DeletedAt is null)).ToList(), nextCursor);
    }

    public void AddGoal(SavingGoal goal) => _context.SavingGoals.Add(goal);

    public void AddContribution(GoalContribution contribution) => _context.GoalContributions.Add(contribution);
}
