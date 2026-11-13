using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <param name="MemberName">Tên hiện tại của người chia sẻ, kể cả người đã rời nhóm (lịch sử vẫn ghi tên).</param>
public sealed record SharedTransactionView(Transaction Transaction, string MemberName, bool IsUserActive);

public sealed record SharedTransactionPage(IReadOnlyList<SharedTransactionView> Items, string? NextCursor);

public sealed record GroupContributionView(GoalContribution Contribution, string MemberName, bool IsUserActive);

public sealed record GroupContributionPage(IReadOnlyList<GroupContributionView> Items, string? NextCursor);

/// <summary>Một khoản thu chi đã chia sẻ, đã xác nhận, không gồm cho vay/mượn; chỉ có những gì báo cáo cần.</summary>
public sealed record GroupReportRow(
    Guid UserId,
    string MemberName,
    bool IsUserActive,
    TransactionType Type,
    long AmountCents,
    Guid? GroupCategoryId,
    string? GroupCategoryName,
    Guid? CategoryId,
    string? CategoryName,
    bool CategoryIsSystem,
    TransactionRelation? Relation = null,
    bool IsReversed = false);

/// <summary>Dữ liệu tài chính của nhóm. Không kiểm quyền: mọi lời gọi phải đi sau <c>IGroupAccessService</c>.</summary>
public interface IGroupFinanceRepository
{
    Task<List<Budget>> GetMatchingBudgetsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default);
    Task<List<Budget>> ListBudgetsAsync(Guid groupId, CancellationToken ct = default);
    Task<Budget?> GetBudgetAsync(Guid groupId, Guid budgetId, CancellationToken ct = default);
    Task<bool> BudgetExistsAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, CancellationToken ct = default);
    Task<List<BudgetPeriod>> ListPeriodsAsync(Guid groupId, DateTimeOffset periodStart, CancellationToken ct = default);
    void AddBudget(Budget budget);

    /// <summary>
    /// Chi ròng đã xác nhận, đã chia sẻ, khớp đúng phạm vi ngân sách (<see cref="Budgets.BudgetSpendScope.Group"/>): chi − hoàn, bỏ cặp đảo và
    /// cho vay/mượn, chặn dưới ở 0. Không truyền danh mục là ngân sách tổng.
    /// </summary>
    Task<long> SumSharedSpendAsync(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Như trên nhưng KHÔNG chặn ở 0 và bỏ các giao dịch đang sửa trong bộ nhớ (caller tự cộng phần của chúng). Phase 81, D2.</summary>
    Task<long> SumSharedNetSpendAsync(System.Linq.Expressions.Expression<Func<Transaction, bool>> scope, IReadOnlyCollection<Guid> excludeTransactionIds, CancellationToken ct = default);

    Task<SharedTransactionPage> ListSharedTransactionsAsync(Guid groupId, Guid? onlyUserId, DateTimeOffset from, DateTimeOffset to, string? cursor, int limit, CancellationToken ct = default);
    Task<List<GroupReportRow>> ListReportRowsAsync(Guid groupId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
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
    Task<GroupContributionPage> ListContributionsAsync(Guid goalId, string? cursor, int limit, CancellationToken ct = default);
    void AddGoal(SavingGoal goal);
    void AddContribution(GoalContribution contribution);
}
