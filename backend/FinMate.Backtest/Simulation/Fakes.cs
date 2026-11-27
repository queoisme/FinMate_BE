using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.FinanceMethods;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Simulation;

/// <summary>
/// Repository giả của backtest (D7). Chỉ những phương thức service dự báo thật sự gọi mới có thân; mọi phương thức khác ném
/// <see cref="NotSupportedException"/>, để một lời gọi ngoài dự kiến hỏng ngay thay vì âm thầm trả rỗng.
/// </summary>
internal static class Unsupported
{
    public static NotSupportedException Call(string member) => new($"backtest: {member} không được gọi trong backtest (D7).");
}

/// <summary>Cùng ngữ nghĩa với <c>ForecastDataRepository</c>, bằng chính các hàm thuần đó (<see cref="RefundNetting"/>, lọc vay).</summary>
public sealed class FakeForecastData(WorldView view) : IForecastDataRepository
{
    public Task<IReadOnlyList<ForecastTransaction>> GetTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(RefundNetting.ForHabits(view.Raw(from, to).Where(t => !t.IsLoan).ToList()));

    public Task<IReadOnlyList<ForecastTransaction>> GetRawTransactionsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(view.Raw(from, to));

    public Task<IReadOnlyList<ForecastTransaction>> GetBoundaryTransfersAsync(
        Guid userId, IReadOnlySet<Guid> countedAccountIds, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(view.Boundary(countedAccountIds, from, to));
}

public sealed class FakeAccounts(WorldView view) : IFinancialAccountRepository
{
    public Task<FinancialAccount?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => Task.FromResult(List().FirstOrDefault(a => a.Id == id));

    public Task<List<FinancialAccount>> GetListByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(List());

    public Task<bool> ExistsByUserAndPackageNameAsync(Guid userId, string packageName, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(ExistsByUserAndPackageNameAsync));

    public Task<FinancialAccount?> GetByUserAndMonitoredPackageAsync(Guid userId, string packageName, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetByUserAndMonitoredPackageAsync));

    public Task AddAsync(FinancialAccount account, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddAsync));

    public Task UpdateAsync(FinancialAccount account, CancellationToken ct = default) => throw Unsupported.Call(nameof(UpdateAsync));

    private List<FinancialAccount> List()
    {
        var created = GeneratorSpec.At(view.World.FirstDay, 0);
        return view.World.Accounts.Select(a => new FinancialAccount
        {
            Id = a.Id, UserId = view.UserId, AccountType = AccountType.Bank, AccountName = a.Name, IsMonitored = true,
            BalanceCents = view.BalanceOf(a.Id), IncludeInForecast = a.IncludeInForecast, CreatedAt = created, UpdatedAt = view.Clock.Now,
        }).ToList();
    }
}

/// <summary>Tầng 1 chỉ có chế độ đáp án: khoản "active" do <paramref name="active"/> cung cấp theo đồng hồ; không có khoản đã hoàn thành.</summary>
public sealed class FakeRecurringItems(Func<IReadOnlyList<RecurringItem>> active) : IRecurringItemRepository
{
    public Task<IReadOnlyList<RecurringItem>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default)
        => Task.FromResult(status is null or RecurringStatus.Active ? active() : (IReadOnlyList<RecurringItem>)[]);

    public Task<RecurringItem?> GetAsync(Guid id, Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetAsync));

    public Task<IReadOnlyList<RecurringItem>> ListAllTrackedAsync(Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(ListAllTrackedAsync));

    public Task<RecurringItem?> FindByCounterpartyAsync(Guid userId, RecurringDirection direction, string counterpartyKey, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(FindByCounterpartyAsync));

    public Task<IReadOnlyDictionary<Guid, int>> CountLivePaymentsAsync(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(CountLivePaymentsAsync));

    public Task<IReadOnlySet<Guid>> LinkedTransactionIdsAsync(Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(LinkedTransactionIdsAsync));

    public void AddPayment(RecurringItemPayment payment) => throw Unsupported.Call(nameof(AddPayment));

    public Task<RecurringItemPayment?> GetPaymentAsync(Guid userId, Guid itemId, Guid paymentId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetPaymentAsync));

    public Task<IReadOnlyList<RecurringItemPayment>> ListPaymentsAsync(Guid userId, Guid itemId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(ListPaymentsAsync));

    public Task<RecurringItemPayment?> FindLivePaymentByTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(FindLivePaymentByTransactionAsync));

    public Task<T> RunWithUserLockedAsync<T>(Guid userId, Func<Task<T>> work, CancellationToken ct = default) => throw Unsupported.Call(nameof(RunWithUserLockedAsync));

    public Task AddAsync(RecurringItem item, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddAsync));

    public Task AddRangeAsync(IEnumerable<RecurringItem> items, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddRangeAsync));

    public Task UpdateAsync(RecurringItem item, CancellationToken ct = default) => throw Unsupported.Call(nameof(UpdateAsync));

    public Task SaveChangesAsync(CancellationToken ct = default) => throw Unsupported.Call(nameof(SaveChangesAsync));
}

/// <summary>Ngân sách người dùng tự đặt (§5.4): một ngân sách mỗi danh mục, có từ ngày đầu dùng app.</summary>
public sealed class FakeBudgets(WorldView view) : IBudgetRepository
{
    public Task<List<Budget>> GetListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var created = GeneratorSpec.At(view.World.FirstDay, 0);
        return Task.FromResult(view.World.Budgets.Select(b => new Budget
        {
            Id = GeneratorSpec.Id(view.UserId, $"budget:{b.CategorySlug}"), UserId = view.UserId, CategoryId = GeneratorSpec.CategoryId(b.CategorySlug),
            LimitCents = b.LimitCents, PeriodType = BudgetPeriodType.Monthly, CreatedAt = created, UpdatedAt = created,
        }).ToList());
    }

    public Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByIdAsync));

    public Task<List<Budget>> GetMatchingBudgetsAsync(Guid userId, Guid? categoryId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetMatchingBudgetsAsync));

    public Task<bool> ExistsAsync(Guid userId, Guid? categoryId, BudgetPeriodType periodType, CancellationToken ct = default) => throw Unsupported.Call(nameof(ExistsAsync));

    public Task AddAsync(Budget budget, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddAsync));

    public Task UpdateAsync(Budget budget, CancellationToken ct = default) => throw Unsupported.Call(nameof(UpdateAsync));

    public Task<BudgetPeriod?> GetPeriodAsync(Guid budgetId, DateTimeOffset periodStart, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetPeriodAsync));

    public Task<List<BudgetPeriod>> GetPeriodsForUserAsync(Guid userId, DateTimeOffset periodStart, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetPeriodsForUserAsync));

    public void AddPeriod(BudgetPeriod period) => throw Unsupported.Call(nameof(AddPeriod));

    public Task<List<BudgetAlertCandidate>> GetPeriodsForAlertAsync(DateTimeOffset now, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetPeriodsForAlertAsync));

    public Task<bool> UpdatePeriodAsync(BudgetPeriod period, CancellationToken ct = default) => throw Unsupported.Call(nameof(UpdatePeriodAsync));
}

public sealed class FakeLoans(WorldView view) : ILoanRepository
{
    public Task<IReadOnlyList<Loan>> ListOpenAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(view.OpenLoans());

    public Task<int> CountUnlinkedLoanTransactionsAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult(view.CountUnlinkedLoanTransactions(since));

    public Task<List<Loan>> GetListAsync(Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetListAsync));

    public Task<Loan?> GetOwnedAsync(Guid id, Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetOwnedAsync));

    public Task AddAsync(Loan loan, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddAsync));

    public Task SaveAsync(CancellationToken ct = default) => throw Unsupported.Call(nameof(SaveAsync));

    public Task<Loan?> GetByClientRequestAsync(Guid userId, Guid clientRequestId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByClientRequestAsync));

    public Task<bool> EntryClientRequestExistsAsync(Guid loanId, Guid clientRequestId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(EntryClientRequestExistsAsync));

    public Task<bool> IsTransactionLinkedAsync(Guid userId, Guid transactionId, CancellationToken ct = default) => throw Unsupported.Call(nameof(IsTransactionLinkedAsync));

    public Task<Loan?> GetByLinkedTransactionAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetByLinkedTransactionAsync));

    public Task<IReadOnlyDictionary<Guid, Application.Loans.LoanLinkDto>> GetLinksAsync(
        Guid userId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetLinksAsync));

    public Task<long> GetSplitInterestCentsAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(GetSplitInterestCentsAsync));

    public void Track(Loan loan) => throw Unsupported.Call(nameof(Track));

    public void Track(Transaction transaction) => throw Unsupported.Call(nameof(Track));

    public void Track(LoanRepayment entry) => throw Unsupported.Call(nameof(Track));

    public Task<IReadOnlyList<Guid>> ListUserIdsWithOpenLoansDueByAsync(DateOnly until, CancellationToken ct = default)
        => throw Unsupported.Call(nameof(ListUserIdsWithOpenLoansDueByAsync));
}

public sealed class FakeConfig(SystemConfigSnapshot snapshot) : ISystemConfigProvider
{
    public Task<SystemConfigSnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(snapshot);

    public void Invalidate()
    {
    }
}

/// <summary>Không đệm: mỗi mốc tính lại, như lần đầu production tính trong ngày.</summary>
public sealed class NoCache : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class UnusedUsers : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByIdAsync));

    public Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByIdIncludingDeletedAsync));

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default) => throw Unsupported.Call(nameof(CountActiveAdminsAsync));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByEmailAsync));

    public Task<User?> GetByFinMateIdAsync(string finMateId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByFinMateIdAsync));

    public Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetByGoogleIdAsync));

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default) => throw Unsupported.Call(nameof(ExistsByEmailAsync));

    public Task AddAsync(User user, CancellationToken ct = default) => throw Unsupported.Call(nameof(AddAsync));

    public Task UpdateAsync(User user, CancellationToken ct = default) => throw Unsupported.Call(nameof(UpdateAsync));

    public Task<UserListResult> GetPageAsync(UserListFilter filter, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetPageAsync));
}

public sealed class UnusedGoals : IGoalPlanningService
{
    public Task<IReadOnlyList<GoalSuggestionDto>> GetSuggestionsAsync(Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetSuggestionsAsync));

    public Task<SpendableDto> GetSpendableAsync(Guid userId, CancellationToken ct = default) => throw Unsupported.Call(nameof(GetSpendableAsync));

    public Task<GoalFeasibilityDto?> PreviewFeasibilityAsync(
        Guid userId, long targetCents, long savedCents, DateTimeOffset deadline, CancellationToken ct = default, GoalContributionFrequency? frequency = null)
        => throw Unsupported.Call(nameof(PreviewFeasibilityAsync));
}
