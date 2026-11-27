using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Một khoản thu/chi giả định trong kịch bản; <paramref name="AmountCents"/> luôn dương, chiều do <paramref name="Direction"/>.</summary>
public sealed record ScenarioItem(DateOnly Date, long AmountCents, RecurringDirection Direction, string Label);

public sealed record CashflowScenarioCommand(Guid UserId, Guid? AccountId, DateOnly? To, IReadOnlyList<ScenarioItem> Items);

public interface IForecastService
{
    Task<CashflowForecastDto> GetCashflowAsync(Guid userId, Guid? accountId, DateOnly? to, CancellationToken ct = default);

    /// <summary>Tính lại theo kịch bản. Không ghi gì vào dữ liệu thật.</summary>
    Task<CashflowForecastDto> RunScenarioAsync(CashflowScenarioCommand command, CancellationToken ct = default);

    Task<SafeToSpendDto> GetSpendableAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Số tiền có thể chi nếu có thêm các khoản giả định trong kỳ. Không ghi gì vào dữ liệu thật.</summary>
    Task<SafeToSpendDto> RunSpendableScenarioAsync(CashflowScenarioCommand command, CancellationToken ct = default);
}

public class ForecastService : IForecastService
{
    private const int MinDefaultHorizonDays = 7;
    private const int ScenarioLabelMaxLength = 60;
    private const int DaysPerMonthUpperBound = 31;

    private readonly IFinancialAccountRepository _accounts;
    private readonly IRecurringItemRepository _items;
    private readonly IForecastDataRepository _data;
    private readonly IBudgetRepository _budgets;
    private readonly IUserRepository _users;
    private readonly IGoalPlanningService _goals;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILoanRepository? _loans;   // null: không đưa sổ vay vào dự báo (test cũ)

    public ForecastService(
        IFinancialAccountRepository accounts,
        IRecurringItemRepository items,
        IForecastDataRepository data,
        IBudgetRepository budgets,
        IUserRepository users,
        IGoalPlanningService goals,
        ISystemConfigProvider config,
        ICacheService cache,
        ILoanRepository? loans = null)
        : this(accounts, items, data, budgets, users, goals, config, cache, () => DateTimeOffset.UtcNow, loans)
    {
    }

    internal ForecastService(
        IFinancialAccountRepository accounts,
        IRecurringItemRepository items,
        IForecastDataRepository data,
        IBudgetRepository budgets,
        IUserRepository users,
        IGoalPlanningService goals,
        ISystemConfigProvider config,
        ICacheService cache,
        Func<DateTimeOffset> now,
        ILoanRepository? loans = null)
    {
        _loans = loans;
        _accounts = accounts;
        _items = items;
        _data = data;
        _budgets = budgets;
        _users = users;
        _goals = goals;
        _config = config;
        _cache = cache;
        _now = now;
    }

    public Task<CashflowForecastDto> GetCashflowAsync(Guid userId, Guid? accountId, DateOnly? to, CancellationToken ct = default)
        => ProjectAsync(userId, accountId, to, [], ct);

    public async Task<CashflowForecastDto> RunScenarioAsync(CashflowScenarioCommand command, CancellationToken ct = default)
    {
        await ValidateScenarioAsync(command, ct);
        return await ProjectAsync(command.UserId, command.AccountId, command.To, command.Items, ct);
    }

    public async Task<SafeToSpendDto> RunSpendableScenarioAsync(CashflowScenarioCommand command, CancellationToken ct = default)
    {
        await ValidateScenarioAsync(command, ct);
        return await SpendableAsync(command.UserId, command.Items, ct);
    }

    private async Task ValidateScenarioAsync(CashflowScenarioCommand command, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        if (command.Items.Count == 0 || command.Items.Count > settings.MaxScenarioItems)
        {
            throw new BusinessRuleException(ForecastErrorCodes.ScenarioInvalid, $"Kịch bản cần từ 1 đến {settings.MaxScenarioItems} khoản giả định.");
        }

        if (command.Items.Any(i => i.AmountCents <= 0 || string.IsNullOrWhiteSpace(i.Label) || i.Label.Trim().Length > ScenarioLabelMaxLength))
        {
            throw new BusinessRuleException(
                ForecastErrorCodes.ScenarioInvalid, $"Mỗi khoản giả định cần số tiền lớn hơn 0 và tên từ 1 đến {ScenarioLabelMaxLength} ký tự.");
        }
    }

    public Task<SafeToSpendDto> GetSpendableAsync(Guid userId, CancellationToken ct = default) => SpendableAsync(userId, null, ct);

    private async Task<SafeToSpendDto> SpendableAsync(Guid userId, IReadOnlyList<ScenarioItem>? scenario, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var rules = ForecastRules.From(settings);
        var today = VietnamTime.DateOf(_now());
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

        var included = (await _accounts.GetListByUserAsync(userId, ct)).Where(a => a.IncludeInForecast).ToList();
        var items = ActiveItemsFor(await _items.ListAsync(userId, RecurringStatus.Active, ct), included.Select(a => a.Id).ToHashSet(), accountId: null);
        var excluded = await ExclusionItemsAsync(userId, items, included.Select(a => a.Id).ToHashSet(), null, today, IrregularLookbackDays(settings), ct);
        var habits = await _data.GetTransactionsAsync(userId, today.AddDays(-rules.HistoryWindowDays), today, ct);
        var recent = BoundaryTransfers.ForRecurring(habits, await BoundaryTransfers.OutflowsAsync(
            _data, userId, included.Select(a => a.Id).ToHashSet(), today.AddDays(-rules.HistoryWindowDays), today, ct));
        var goals = await _goals.GetSpendableAsync(userId, ct);
        var irregular = await IrregularIncomeAsync(userId, items, excluded, today, settings, rules, ct);
        var loans = await OpenLoansAsync(userId, ct);

        return SpendableCalculator.Compute(new SpendableInput(
            today,
            included.Select(a => new SpendableAccount(a.Id, a.AccountName, a.BalanceCents)).ToList(),
            items,
            recent,
            goals.GoalSetAsideCents,
            goals.Goals,
            Reserve(user, settings),
            rules,
            irregular,
            loans,
            scenario)) with
        {
            UndatedLoans = LoanForecast.Undated(loans),
            UnlinkedLoanTransactions = await UnlinkedLoansAsync(userId, ct),
        };
    }

    /// <summary>Phase 90: khoản vay <c>open</c> của user; rỗng khi không nối sổ vay.</summary>
    private async Task<IReadOnlyList<Loan>> OpenLoansAsync(Guid userId, CancellationToken ct)
        => _loans is null ? [] : await _loans.ListOpenAsync(userId, ct);

    /// <summary>S5: giao dịch loan đã xác nhận trong <c>loans.suggestWindowHours × 10</c> giờ mà chưa gắn khoản vay: có thể dự báo đang trừ hai lần.</summary>
    private async Task<UnlinkedLoanTransactionsDto?> UnlinkedLoansAsync(Guid userId, CancellationToken ct)
    {
        if (_loans is null)
        {
            return null;
        }

        var window = (await _config.GetAsync(ct)).Document.Loans.SuggestWindowHours * 10;
        var count = await _loans.CountUnlinkedLoanTransactionsAsync(userId, _now().AddHours(-window), ct);
        return count == 0 ? null : new UnlinkedLoanTransactionsDto(count);
    }

    /// <summary>
    /// Không có khoản thu định kỳ nào: thu nhập không đều mức thận trọng còn chờ trong tháng (sơ đồ trang 10: "Cộng khoản thu dự kiến ở mức
    /// thận trọng"), đã trừ phần nhận trong tháng — phần đó đã nằm trong số dư.
    /// </summary>
    /// <param name="excluded">Phase 92 (D5): khoản đang theo dõi + khoản vừa hoàn thành, để loại giao dịch khớp chúng khỏi thu không đều.</param>
    private async Task<long> IrregularIncomeAsync(
        Guid userId, IReadOnlyList<RecurringItemView> items, IReadOnlyList<RecurringItemView> excluded, DateOnly today, ForecastSettings settings,
        ForecastRules rules, CancellationToken ct)
    {
        if (items.Any(i => i.Direction == RecurringDirection.Inflow))
        {
            return 0;
        }

        var lookback = IrregularLookbackDays(settings);
        var history = await _data.GetTransactionsAsync(userId, today.AddDays(-lookback), today, ct);
        var estimate = IncomeEstimator.Irregular(history, excluded, today, settings.IrregularIncomeMonths, rules);
        return estimate is null ? 0 : Math.Max(0, estimate.ConservativeMonthlyCents - estimate.ReceivedThisMonthCents);
    }

    private static int IrregularLookbackDays(ForecastSettings settings) => (settings.IrregularIncomeMonths + 1) * DaysPerMonthUpperBound;

    /// <summary>Phase 92 (D5): khoản dùng để loại giao dịch (đang theo dõi + đã hoàn thành trong cửa sổ), cùng phạm vi ví với danh sách chiếu.</summary>
    private async Task<IReadOnlyList<RecurringItemView>> ExclusionItemsAsync(
        Guid userId, IReadOnlyList<RecurringItemView> active, IReadOnlySet<Guid> accountIds, Guid? accountId, DateOnly today, int windowDays, CancellationToken ct)
    {
        var completed = (await _items.ListAsync(userId, RecurringStatus.Completed, ct))
            .Where(i => accountId is { } id ? i.FinancialAccountId == id : i.FinancialAccountId is null || accountIds.Contains(i.FinancialAccountId.Value));
        return RecurringExclusions.With(active, completed, today, windowDays);
    }

    /// <summary>Dự phòng (BR-03.3): số user tự đặt, không thì % thu nhập dự kiến; chưa khai thu nhập thì 0.</summary>
    internal static long Reserve(User user, ForecastSettings settings)
        => user.ForecastReserveCents
           ?? (user.MonthlyIncomeCents is > 0 and var income ? income * settings.ReservePercentOfIncome / 100 : 0);

    private async Task<CashflowForecastDto> ProjectAsync(
        Guid userId, Guid? accountId, DateOnly? to, IReadOnlyList<ScenarioItem> scenario, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var rules = ForecastRules.From(settings);
        var today = VietnamTime.DateOf(_now());
        var end = ResolveEnd(today, to, settings.MaxHorizonDays);
        if (scenario.Any(i => i.Date < today || i.Date > end))
        {
            throw new BusinessRuleException(ForecastErrorCodes.ScenarioInvalid, "Khoản giả định phải nằm trong kỳ dự báo.");
        }

        var accounts = await AccountsInScopeAsync(userId, accountId, ct);
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var items = ActiveItemsFor(await _items.ListAsync(userId, RecurringStatus.Active, ct), accountIds, accountId);
        var transactions = (await _data.GetTransactionsAsync(userId, today.AddDays(-rules.HistoryWindowDays), today, ct))
            .Where(t => accountIds.Contains(t.AccountId))
            .ToList();

        var excluded = await ExclusionItemsAsync(userId, items, accountIds, accountId, today, rules.HistoryWindowDays, ct);
        var baseline = await BaselineAsync(userId, accountId, transactions, excluded, today, rules, ct);
        var spentToday = transactions
            .Where(t => t.Date == today && t.Type == TransactionType.Debit && !t.ExcludedFromForecast && !RecurringMatcher.MatchesAny(t, excluded, rules))
            .Sum(t => t.AmountCents);

        var boundary = await BoundaryTransfers.OutflowsAsync(_data, userId, accountIds, today.AddDays(-rules.HistoryWindowDays), today, ct);
        var events = CashflowEngine.RecurringEvents(items, BoundaryTransfers.ForRecurring(transactions, boundary), today, end, rules)
            .Concat(scenario.Select(i => new CashflowEvent(
                i.Date, i.Direction == RecurringDirection.Inflow ? i.AmountCents : -i.AmountCents, i.Label.Trim(), CashflowEventKinds.Scenario, null)))
            .ToList();

        // Phase 90: sổ vay không thuộc ví nào, nên chỉ vào dự báo toàn bộ (không vào dự báo của một ví).
        var loans = accountId is null ? await OpenLoansAsync(userId, ct) : [];
        events.AddRange(LoanForecast.Events(loans, today, end));

        return CashflowEngine.Project(new CashflowInput(
            today, end, accounts.Sum(a => a.BalanceCents), baseline, spentToday, events, settings.Holidays)) with
        {
            UndatedLoans = LoanForecast.Undated(loans),
            UnlinkedLoanTransactions = accountId is null ? await UnlinkedLoansAsync(userId, ct) : null,
        };
    }

    /// <summary>Mặc định tới cuối tháng; còn dưới một tuần thì tới cuối tháng sau, để biểu đồ không chỉ có vài điểm.</summary>
    private static DateOnly ResolveEnd(DateOnly today, DateOnly? to, int maxHorizonDays)
    {
        if (to is { } wanted)
        {
            if (wanted < today || wanted > today.AddDays(maxHorizonDays))
            {
                throw new BusinessRuleException(
                    ForecastErrorCodes.HorizonInvalid, $"Ngày kết thúc dự báo phải từ hôm nay tới tối đa {maxHorizonDays} ngày sau.");
            }

            return wanted;
        }

        var monthEnd = MonthEnd(today);
        return monthEnd.DayNumber - today.DayNumber + 1 >= MinDefaultHorizonDays ? monthEnd : MonthEnd(today.AddMonths(1));
    }

    private static DateOnly MonthEnd(DateOnly day) => new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    /// <summary>Một ví cụ thể (của chính user, bất kể có được tính vào tổng hay không), hoặc mọi ví đang được tính.</summary>
    private async Task<IReadOnlyList<FinancialAccount>> AccountsInScopeAsync(Guid userId, Guid? accountId, CancellationToken ct)
    {
        if (accountId is { } id)
        {
            var account = await _accounts.GetByIdAsync(id, userId, ct) ?? throw new NotFoundException("FinancialAccount", id);
            return [account];
        }

        return (await _accounts.GetListByUserAsync(userId, ct)).Where(a => a.IncludeInForecast).ToList();
    }

    /// <summary>
    /// Khoản định kỳ thuộc phạm vi đang xem: xem một ví thì chỉ khoản gắn với ví đó; xem tổng thì khoản không gắn ví hoặc gắn với ví đang được tính.
    /// </summary>
    internal static IReadOnlyList<RecurringItemView> ActiveItemsFor(IReadOnlyList<RecurringItem> active, IReadOnlySet<Guid> accountIds, Guid? accountId)
        => active
            .Where(i => accountId is { } id
                ? i.FinancialAccountId == id
                : i.FinancialAccountId is null || accountIds.Contains(i.FinancialAccountId.Value))
            .Select(i => i.ToView())
            .ToList();

    /// <summary>Chỉ đệm cho góc nhìn tổng (màn hình mở nhiều nhất); xem từng ví thì tính lại, vì đó là thao tác hiếm.</summary>
    private async Task<SpendingBaseline> BaselineAsync(
        Guid userId,
        Guid? accountId,
        IReadOnlyList<ForecastTransaction> transactions,
        IReadOnlyList<RecurringItemView> items,
        DateOnly today,
        ForecastRules rules,
        CancellationToken ct)
    {
        var key = CacheKeys.ForecastBaseline(userId, today);
        if (accountId is null && await _cache.GetAsync<SpendingBaseline>(key, ct) is { } cached)
        {
            return cached;
        }

        var budgetTotal = MonthlyBudgetTotal(await _budgets.GetListForUserAsync(userId, ct));
        var baseline = SpendingBaselineCalculator.Compute(transactions.Where(t => t.Date < today).ToList(), items, budgetTotal, today, rules);
        if (accountId is null)
        {
            await _cache.SetAsync(key, baseline, CacheKeys.ForecastBaselineTtl, ct);
        }

        return baseline;
    }

    /// <summary>
    /// Hạn mức tổng nếu user có đặt (nó bao trùm mọi danh mục), không thì tổng hạn mức các danh mục. Cộng cả hai là tính trùng mọi khoản chi.
    /// </summary>
    internal static long MonthlyBudgetTotal(IReadOnlyList<Budget> budgets)
        => budgets.FirstOrDefault(b => b.CategoryId is null) is { } total ? total.LimitCents : budgets.Sum(b => b.LimitCents);

    private async Task<ForecastSettings> SettingsAsync(CancellationToken ct) => (await _config.GetAsync(ct)).Document.Forecast;
}
