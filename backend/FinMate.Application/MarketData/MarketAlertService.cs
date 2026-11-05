using System.Globalization;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.MarketData;

public interface IMarketAlertTrigger
{
    void Enqueue();
}

public interface IMarketAlertService
{
    Task ProcessDueAsync(CancellationToken ct = default);
}

/// <summary>
/// Quy tắc của cảnh báo giá tăng, tách khỏi I/O. Thói quen chi nằm ở <see cref="MarketSpendProfile"/>.
/// Nội dung là mẫu cố định: không chữ nào đến từ trang web hay từ AI, ngoài tên nguồn do admin đặt.
/// </summary>
public static class MarketAlertRules
{
    private const long RoundingCents = 1_000;
    private const int DaysPerMonth = 30;
    private const int MorningHour = 8;
    private const int EveningHour = 21;

    /// <summary>Giá tăng từ ngưỡng trở lên. Làm tính bằng số nguyên để không lệch ở đúng mức ngưỡng.</summary>
    public static bool RoseEnough(long reference, long latest, int thresholdPercent)
        => reference > 0 && (latest - reference) * 100 >= reference * thresholdPercent;

    /// <summary>Chi xăng trung bình một tháng nhân mức tăng, làm tròn tới 1.000đ.</summary>
    public static long ExtraCostCents(long lookbackSpendCents, int lookbackDays, long reference, long latest)
    {
        var monthly = lookbackSpendCents * DaysPerMonth / lookbackDays;
        var extra = monthly * (latest - reference) / reference;
        return (extra + (RoundingCents / 2)) / RoundingCents * RoundingCents;
    }

    public static bool BudgetIsTight(BudgetForecastDto budget, AlertThresholds thresholds)
        => budget.Status is BudgetRiskStatus.AtRisk or BudgetRiskStatus.Over || UsedPercent(budget) >= thresholds.T1Pct;

    public static int UsedPercent(BudgetForecastDto budget)
        => budget.LimitCents <= 0 ? 0 : (int)(budget.SpentCents * 100 / budget.LimitCents);

    /// <summary>Không đẩy thông báo lúc người ta đang ngủ, vì giá xăng có thể đổi lúc nửa đêm.</summary>
    public static DateTimeOffset? NotBefore(DateTimeOffset now)
    {
        var hour = VietnamTime.ToLocal(now).Hour;
        return hour is >= MorningHour and < EveningHour ? null : ForecastAlertKinds.NextMorning(now);
    }

    public static string FuelLabel(string fuelKey) => fuelKey switch
    {
        FuelKeys.E10Ron95Level5 or FuelKeys.E10Ron95Level3 => "xăng RON 95",
        FuelKeys.E5Ron92Level2 => "xăng RON 92",
        FuelKeys.Diesel0001Level5 or FuelKeys.Diesel005Level2 => "dầu diesel",
        _ => "xăng dầu",
    };

    /// <param name="comparedDays">Null khi chưa có giá từ đủ lâu: chỉ so được với mức giá trước đó.</param>
    public static string FuelBody(string fuelKey, long increase, int usedPercent, long extraCost, string categoryName, int? comparedDays)
    {
        var compared = comparedDays is { } days ? $"{days} ngày trước" : "mức giá trước đó";
        var body = $"Giá {FuelLabel(fuelKey)} tăng khoảng {ForecastText.Money(increase)}/lít so với {compared}. Ngân sách {categoryName} của bạn đã dùng {usedPercent}%.";
        if (extraCost > 0)
        {
            body += $" Với mức đổ xăng như mọi tháng, bạn có thể tốn thêm khoảng {ForecastText.Money(extraCost)}.";
        }

        return body + " Gộp các chuyến đi gần nhau sẽ giúp bạn giữ trong hạn mức.";
    }

    public static string Title(MarketCostType type) => $"Giá {type.Label} đang tăng";

    public static string NewsBody(MarketCostType type, int usedPercent, string budgetName, string sourceName)
        => $"Có tin giá {type.Label} tăng. Ngân sách {budgetName} của bạn đã dùng {usedPercent}%, bạn nên để dư thêm cho kỳ tới. Nguồn: {sourceName}.";
}

public sealed class MarketAlertService : IMarketAlertService
{
    private readonly IMarketDataRepository _market;
    private readonly IReportRepository _reports;
    private readonly IBudgetForecastService _budgets;
    private readonly IForecastAlertRepository _alerts;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly IAlertThresholdProvider _thresholds;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<MarketAlertService> _logger;

    public MarketAlertService(
        IMarketDataRepository market,
        IReportRepository reports,
        IBudgetForecastService budgets,
        IForecastAlertRepository alerts,
        IForecastAlertDispatcher dispatcher,
        IAlertThresholdProvider thresholds,
        ISystemConfigProvider config,
        ILogger<MarketAlertService> logger)
    {
        _market = market;
        _reports = reports;
        _budgets = budgets;
        _alerts = alerts;
        _dispatcher = dispatcher;
        _thresholds = thresholds;
        _config = config;
        _logger = logger;
    }

    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        var document = (await _config.GetAsync(ct)).Document;
        var settings = document.MarketData;
        var enabled = document.Features.MarketAlerts;
        var now = DateTimeOffset.UtcNow;

        await ProcessFuelAsync(settings, enabled, now, ct);
        foreach (var marketEvent in await _market.ListEventsAwaitingAlertsAsync(ct))
        {
            var type = settings.CostTypes.FirstOrDefault(t => t.Key == MarketEventText.Topic(marketEvent.Topic) && t.Enabled);
            if (enabled && type is not null && marketEvent.Direction == MarketDirection.Up)
            {
                await SendNewsAlertsAsync(marketEvent, type, settings, now, ct);
            }

            // Cờ tắt vẫn đánh dấu đã xét: bật cờ không được gửi bù hàng loạt tin đã cũ.
            marketEvent.AlertsProcessedAt = now;
            await _market.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessFuelAsync(MarketDataSettings settings, bool enabled, DateTimeOffset now, CancellationToken ct)
    {
        var waiting = await _market.ListFuelPricesAwaitingAlertsAsync(settings.ReferenceFuelKey, ct);
        if (waiting.Count == 0)
        {
            return;
        }

        var type = settings.CostTypes.FirstOrDefault(t => t.HasPriceTable && t.Enabled);
        enabled &= type is not null;
        var latest = waiting[^1];
        var reference = await _market.GetFuelPriceAsOfAsync(settings.ReferenceFuelKey, now.AddDays(-settings.CompareWindowDays), ct);

        // So với 14 ngày trước có thể vẫn cao hơn trong khi giá vừa giảm. Khi đó không được báo là đang tăng.
        var previous = await _market.GetFuelPriceBeforeAsync(settings.ReferenceFuelKey, latest.FirstSeenAt, latest.Id, ct);
        if (enabled && reference is not null && reference.Id != latest.Id
            && previous is not null && latest.Region1PriceCents > previous.Region1PriceCents
            && MarketAlertRules.RoseEnough(reference.Region1PriceCents, latest.Region1PriceCents, settings.RiseThresholdPercent))
        {
            await ForEachActiveUserAsync(settings, now, (userId, today) => SendFuelAlertAsync(userId, today, type!, latest, reference, settings, now, ct), ct);
        }

        foreach (var price in waiting)
        {
            price.AlertsProcessedAt = now;
        }

        await _market.SaveChangesAsync(ct);
    }

    private async Task ForEachActiveUserAsync(MarketDataSettings settings, DateTimeOffset now, Func<Guid, DateOnly, Task> send, CancellationToken ct)
    {
        var today = VietnamTime.DateOf(now);
        foreach (var userId in await _reports.GetUserIdsWithRecentActivityAsync(today.AddDays(-settings.SpendLookbackDays), today, ct))
        {
            try
            {
                await send(userId, today);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Market alert failed for user {UserId}: {ErrorType}", userId, ex.GetType().Name);
            }
        }
    }

    private async Task SendFuelAlertAsync(Guid userId, DateOnly today, MarketCostType type, FuelPrice latest, FuelPrice reference, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var prefix = KeyPrefix(type);
        if (await InCooldownAsync(userId, prefix, settings, now, ct))
        {
            return;
        }

        var target = await FindTargetAsync(userId, today, type, settings, now, ct);
        if (target is null)
        {
            return;
        }

        var (profile, budget) = target.Value;
        var increase = latest.Region1PriceCents - reference.Region1PriceCents;
        var extra = MarketAlertRules.ExtraCostCents(profile.TotalCents, settings.SpendLookbackDays, reference.Region1PriceCents, latest.Region1PriceCents);
        if (extra < settings.MinExtraCostCents)
        {
            return;
        }

        await _dispatcher.RaiseAsync(
            new ForecastAlertRequest(
                userId,
                ForecastAlertKind.MarketChange,
                $"{prefix}{latest.Id}",
                MarketAlertRules.Title(type),
                MarketAlertRules.FuelBody(latest.FuelKey, increase, MarketAlertRules.UsedPercent(budget), extra, BudgetName(budget), reference.FirstSeenAt <= now.AddDays(-settings.CompareWindowDays) ? settings.CompareWindowDays : null),
                Payload(type, budget, profile, extra),
                NotBefore: MarketAlertRules.NotBefore(now)),
            ct);
    }

    private async Task SendNewsAlertsAsync(MarketEvent marketEvent, MarketCostType type, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var sources = await _market.ListEventSignalsAsync(marketEvent.Topic, marketEvent.Direction, marketEvent.FirstSeenAt.AddDays(-1), ct);
        var sourceName = sources.Count == 0 ? "tin tức" : sources[0].SourceName;
        var prefix = KeyPrefix(type);
        await ForEachActiveUserAsync(
            settings,
            now,
            async (userId, today) =>
            {
                if (await InCooldownAsync(userId, prefix, settings, now, ct))
                {
                    return;
                }

                var target = await FindTargetAsync(userId, today, type, settings, now, ct);
                if (target is null)
                {
                    return;
                }

                var (profile, budget) = target.Value;
                await _dispatcher.RaiseAsync(
                    new ForecastAlertRequest(
                        userId,
                        ForecastAlertKind.MarketChange,
                        $"{prefix}{marketEvent.Id}",
                        MarketAlertRules.Title(type),
                        MarketAlertRules.NewsBody(type, MarketAlertRules.UsedPercent(budget), BudgetName(budget), sourceName),
                        Payload(type, budget, profile, null),
                        NotBefore: MarketAlertRules.NotBefore(now)),
                    ct);
            },
            ct);
    }

    /// <summary>Người này có thật sự chi cho khoản đó không, và ngân sách chứa khoản đó có đang căng không. Xét theo giao dịch nên danh mục đặt tên gì cũng được.</summary>
    private async Task<(MarketSpendProfile Profile, BudgetForecastDto Budget)?> FindTargetAsync(Guid userId, DateOnly today, MarketCostType type, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var debits = await _reports.GetConfirmedDebitsAsync(userId, today.AddDays(-settings.SpendLookbackDays), today, ct);
        var profile = MarketSpendProfile.Build(debits, type, settings.SpendLookbackDays);
        if (profile is null || !profile.IsRelevant(settings, now))
        {
            return null;
        }

        var budget = await TightBudgetAsync(userId, profile.MainCategoryId, ct);
        return budget is null ? null : (profile, budget);
    }

    /// <summary>Ngân sách của danh mục chứa khoản chi, không có thì ngân sách tổng. Danh mục đã có ngân sách còn dư thì không xét ngân sách tổng.</summary>
    private async Task<BudgetForecastDto?> TightBudgetAsync(Guid userId, Guid? categoryId, CancellationToken ct)
    {
        var budgets = await _budgets.GetAsync(userId, ct);
        var budget = budgets.FirstOrDefault(b => categoryId is not null && b.CategoryId == categoryId)
            ?? budgets.FirstOrDefault(b => b.CategoryId is null);
        if (budget is null)
        {
            return null;
        }

        var thresholds = await _thresholds.GetAsync(userId, ct);
        return MarketAlertRules.BudgetIsTight(budget, thresholds) ? budget : null;
    }

    private static string BudgetName(BudgetForecastDto budget) => budget.CategoryName ?? "tổng chi tiêu";

    /// <summary>Số tiền nằm trong payload gửi app, không bao giờ ghi vào log.</summary>
    private static Dictionary<string, string> Payload(MarketCostType type, BudgetForecastDto budget, MarketSpendProfile profile, long? extra)
    {
        var data = new Dictionary<string, string>
        {
            ["topic"] = type.Key,
            ["budgetId"] = budget.BudgetId.ToString(),
            ["monthlySpendCents"] = profile.MonthlyCents.ToString(CultureInfo.InvariantCulture),
            ["timesPerMonth"] = profile.TimesPerMonth.ToString(CultureInfo.InvariantCulture),
        };
        if (extra is { } amount)
        {
            data["extraCostCents"] = amount.ToString(CultureInfo.InvariantCulture);
        }

        return data;
    }

    private Task<bool> InCooldownAsync(Guid userId, string prefix, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
        => _alerts.AnyWithKeyPrefixSinceAsync(userId, ForecastAlertKind.MarketChange, prefix, now.AddDays(-settings.AlertCooldownDays), ct);

    private static string KeyPrefix(MarketCostType type) => $"market:{type.Key}:";
}
