using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public interface IRecurringDetectionService
{
    /// <summary>Cập nhật khoản định kỳ đã có và thêm khoản mới phát hiện cho một user. Trả số khoản mới.</summary>
    Task<int> RunForUserAsync(Guid userId, CancellationToken ct = default);
}

public class RecurringDetectionService : IRecurringDetectionService
{
    private readonly IRecurringItemRepository _items;
    private readonly IForecastDataRepository _data;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;
    private readonly IForecastAlertDispatcher _alerts;
    private readonly Func<DateTimeOffset> _now;

    public RecurringDetectionService(
        IRecurringItemRepository items, IForecastDataRepository data, ISystemConfigProvider config, ICacheService cache, IForecastAlertDispatcher alerts)
        : this(items, data, config, cache, alerts, () => DateTimeOffset.UtcNow)
    {
    }

    internal RecurringDetectionService(
        IRecurringItemRepository items,
        IForecastDataRepository data,
        ISystemConfigProvider config,
        ICacheService cache,
        IForecastAlertDispatcher alerts,
        Func<DateTimeOffset> now)
    {
        _items = items;
        _data = data;
        _config = config;
        _cache = cache;
        _alerts = alerts;
        _now = now;
    }

    public async Task<int> RunForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = _now();
        var today = VietnamTime.DateOf(now);
        var rules = ForecastRules.From((await _config.GetAsync(ct)).Document.Forecast);
        var history = await _data.GetTransactionsAsync(userId, today.AddDays(-rules.HistoryWindowDays), today, ct);
        var existing = await _items.ListAllTrackedAsync(userId, ct);

        var changed = false;
        var priceChanges = new List<PriceChange>();
        foreach (var item in existing)
        {
            changed |= RecurringTracker.Advance(item, history, rules, now, priceChanges);
        }

        var known = existing
            .Where(i => i.CounterpartyKey is not null)
            .Select(i => (i.Direction, i.CounterpartyKey!))
            .ToHashSet();
        var detected = RecurringDetector.Detect(history, known, today, rules);
        if (detected.Count > 0)
        {
            await _items.AddRangeAsync(detected.Select(d => ToEntity(userId, d, now)), ct);
        }

        if (changed || detected.Count > 0)
        {
            await _items.SaveChangesAsync(ct);
            await _cache.RemoveAsync(CacheKeys.ForecastBaseline(userId, today), ct);
        }

        await AnnouncePriceChangesAsync(userId, priceChanges, now, ct);
        return detected.Count;
    }

    /// <summary>Sơ đồ 5a: "Tạo thông báo thay đổi giá". Job chạy ban đêm nên hẹn tới 08:00 mới gửi.</summary>
    private async Task AnnouncePriceChangesAsync(Guid userId, IReadOnlyList<PriceChange> changes, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            await _alerts.RaiseAsync(new ForecastAlertRequest(
                userId,
                ForecastAlertKind.PriceChange,
                $"price_change:{change.RecurringItemId}:{ForecastText.Iso(change.PaidOn)}",
                $"{change.Name} đổi giá",
                $"Từ {ForecastText.Money(change.OldAmountCents)} thành {ForecastText.Money(change.NewAmountCents)}. Dự báo đã theo giá mới.",
                new Dictionary<string, string>
                {
                    ["recurringItemId"] = change.RecurringItemId.ToString(),
                    ["oldAmountCents"] = change.OldAmountCents.ToString(),
                    ["newAmountCents"] = change.NewAmountCents.ToString(),
                },
                NotBefore: ForecastAlertKinds.NextMorning(now)), ct);
        }
    }

    private static RecurringItem ToEntity(Guid userId, DetectedRecurring d, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Direction = d.Direction,
        Name = d.Name,
        CounterpartyKey = d.CounterpartyKey,
        CategoryId = d.CategoryId,
        FinancialAccountId = d.AccountId,
        AmountCents = d.AmountCents,
        Cadence = d.Cadence,
        NextDueDate = d.NextDueDate,
        Status = d.Status,
        Source = RecurringSource.Detected,
        Occurrences = d.Occurrences,
        LastSeenAt = d.LastSeenAt,
        LastSeenAmountCents = d.LastSeenAmountCents,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
