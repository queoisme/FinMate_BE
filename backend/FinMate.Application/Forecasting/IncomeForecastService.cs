using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public interface IIncomeForecastService
{
    Task<IncomeForecastDto> GetAsync(Guid userId, CancellationToken ct = default);
}

public class IncomeForecastService : IIncomeForecastService
{
    private const int UpcomingDays = 60;
    private const int DaysPerMonthUpperBound = 31;

    private readonly IRecurringItemRepository _items;
    private readonly IForecastDataRepository _data;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public IncomeForecastService(IRecurringItemRepository items, IForecastDataRepository data, ISystemConfigProvider config)
        : this(items, data, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal IncomeForecastService(IRecurringItemRepository items, IForecastDataRepository data, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _items = items;
        _data = data;
        _config = config;
        _now = now;
    }

    public async Task<IncomeForecastDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var (settings, rules, today, items, history) = await LoadAsync(userId, ct);
        var regular = items.Any(i => i.Direction == RecurringDirection.Inflow);
        return new IncomeForecastDto(
            regular,
            IncomeEstimator.Upcoming(items, history, today, today.AddDays(UpcomingDays), rules),
            IncomeEstimator.Late(items, history, today, settings.IncomeLateDays, rules),
            regular ? null : IncomeEstimator.Irregular(history, items, today, settings.IrregularIncomeMonths, rules));
    }

    private async Task<(Admin.SystemConfig.ForecastSettings Settings, ForecastRules Rules, DateOnly Today, IReadOnlyList<RecurringItemView> Items, IReadOnlyList<ForecastTransaction> History)>
        LoadAsync(Guid userId, CancellationToken ct)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var rules = ForecastRules.From(settings);
        var today = VietnamTime.DateOf(_now());
        var items = (await _items.ListAsync(userId, RecurringStatus.Active, ct)).Select(i => i.ToView()).ToList();
        var lookback = Math.Max(rules.HistoryWindowDays, (settings.IrregularIncomeMonths + 1) * DaysPerMonthUpperBound);
        var history = await _data.GetTransactionsAsync(userId, today.AddDays(-lookback), today, ct);
        return (settings, rules, today, items, history);
    }
}
