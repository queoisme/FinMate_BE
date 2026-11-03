using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public interface IBudgetForecastService
{
    /// <summary>Tiến độ và mức chi dự kiến của từng ngân sách kỳ hiện tại (sơ đồ trang 6: "Xem tiến độ và mức chi dự kiến của từng danh mục").</summary>
    Task<IReadOnlyList<BudgetForecastDto>> GetAsync(Guid userId, CancellationToken ct = default);
}

public class BudgetForecastService : IBudgetForecastService
{
    private readonly IBudgetRepository _budgets;
    private readonly IRecurringItemRepository _items;
    private readonly IForecastDataRepository _data;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public BudgetForecastService(IBudgetRepository budgets, IRecurringItemRepository items, IForecastDataRepository data, ISystemConfigProvider config)
        : this(budgets, items, data, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal BudgetForecastService(
        IBudgetRepository budgets, IRecurringItemRepository items, IForecastDataRepository data, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _budgets = budgets;
        _items = items;
        _data = data;
        _config = config;
        _now = now;
    }

    public async Task<IReadOnlyList<BudgetForecastDto>> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var budgets = await _budgets.GetListForUserAsync(userId, ct);
        if (budgets.Count == 0)
        {
            return [];
        }

        var now = _now();
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var rules = ForecastRules.From(settings);
        var today = VietnamTime.DateOf(now);
        var (periodStartInstant, _) = VietnamTime.MonthRange(now);
        var periodStart = new DateOnly(today.Year, today.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var periods = (await _budgets.GetPeriodsForUserAsync(userId, periodStartInstant, ct)).ToDictionary(p => p.BudgetId);
        var itemEntities = await _items.ListAsync(userId, RecurringStatus.Active, ct);
        var items = itemEntities.Select(i => i.ToView()).ToList();
        var itemCategories = itemEntities.ToDictionary(i => i.Id, i => i.CategoryId);
        var history = await _data.GetTransactionsAsync(userId, today.AddDays(-rules.HistoryWindowDays), today, ct);
        var upcoming = CashflowEngine.RecurringEvents(items, history, today, periodEnd, rules);

        return budgets
            .Select(b =>
            {
                var period = periods.GetValueOrDefault(b.Id);
                var input = new BudgetRiskInput(
                    b.Id, b.CategoryId, b.Category?.Name, period?.LimitCents ?? b.LimitCents, period?.SpentCents ?? 0, periodStart, periodEnd);
                return BudgetRiskCalculator.Compute(input, history, items, upcoming, itemCategories, today, rules, settings.BudgetRiskMinElapsedDays);
            })
            .ToList();
    }
}
