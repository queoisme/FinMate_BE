using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <param name="MemberName">Tên hiện tại của người chia sẻ, kể cả người đã rời nhóm (lịch sử vẫn ghi tên).</param>
public sealed record SharedTransactionView(Transaction Transaction, string MemberName, bool IsUserActive);

public sealed record GroupContributionView(GoalContribution Contribution, string MemberName, bool IsUserActive);

/// <summary>Dữ liệu tài chính của nhóm. Không kiểm quyền: mọi lời gọi phải đi sau <c>IGroupAccessService</c>.</summary>
public interface IGroupFinanceRepository
{
    Task<List<Budget>> GetMatchingBudgetsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default);
    Task<List<Budget>> ListBudgetsAsync(Guid groupId, CancellationToken ct = default);
    Task<Budget?> GetBudgetAsync(Guid groupId, Guid budgetId, CancellationToken ct = default);
    Task<bool> BudgetExistsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default);
    Task<List<BudgetPeriod>> ListPeriodsAsync(Guid groupId, DateTimeOffset periodStart, CancellationToken ct = default);
    void AddBudget(Budget budget);

    /// <summary>Tổng khoản chi đã xác nhận, đã chia sẻ, khớp đúng phạm vi ngân sách; loại cho vay/mượn. Không truyền danh mục là ngân sách tổng.</summary>
    Task<long> SumSharedSpendAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<List<SharedTransactionView>> ListSharedTransactionsAsync(Guid groupId, Guid? onlyUserId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default);
    Task<Transaction?> GetSharedTransactionAsync(Guid groupId, Guid transactionId, CancellationToken ct = default);

    Task<List<GroupCategory>> ListCategoriesAsync(Guid groupId, CancellationToken ct = default);
    Task<GroupCategory?> GetCategoryAsync(Guid groupId, Guid categoryId, CancellationToken ct = default);
    Task<bool> CategorySlugExistsAsync(Guid groupId, string slug, CancellationToken ct = default);
    Task<bool> CategoryHasActiveBudgetAsync(Guid categoryId, CancellationToken ct = default);
    void AddCategory(GroupCategory category);

    Task<List<GroupShareRule>> ListActiveRulesForUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<GroupShareRule>> ListRulesAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupShareRule?> GetRuleAsync(Guid groupId, Guid userId, Guid ruleId, CancellationToken ct = default);
    void AddRule(GroupShareRule rule);

    Task<List<SavingGoal>> ListGoalsAsync(Guid groupId, CancellationToken ct = default);
    Task<SavingGoal?> GetGoalAsync(Guid groupId, Guid goalId, CancellationToken ct = default);
    Task<List<GroupContributionView>> ListContributionsAsync(Guid goalId, int limit, CancellationToken ct = default);
    void AddGoal(SavingGoal goal);
    void AddContribution(GoalContribution contribution);
}
