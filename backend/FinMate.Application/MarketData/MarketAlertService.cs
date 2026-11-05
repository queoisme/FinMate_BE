using System.Globalization;
using System.Text;
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
/// Quy tắc chọn người nhận cảnh báo giá tăng, tách khỏi I/O. Cần có đủ: có chi cho danh mục đó, có ngân sách danh mục đó, và ngân sách đang căng.
/// Nội dung là mẫu cố định: không chữ nào đến từ trang web hay từ AI, ngoài tên nguồn do admin đặt.
/// </summary>
public static class MarketAlertRules
{
    private const long RoundingCents = 1_000;
    private const int DaysPerMonth = 30;
    private const int MorningHour = 8;
    private const int EveningHour = 21;

    private static readonly string[] FuelKeywords = ["xăng", "petrolimex", "pvoil", "mipec", "petro", "cửa hàng xăng dầu", "chxd"];

    public static bool IsFuelPurchase(Transaction transaction)
    {
        var text = $"{transaction.MerchantName} {transaction.Description}".ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return FuelKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
    }

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

    public static (string Title, string Subject)? NewsTemplate(MarketTopic topic) => topic switch
    {
        MarketTopic.Electricity => ("Giá điện đang tăng", "giá điện"),
        MarketTopic.Water => ("Giá nước đang tăng", "giá nước"),
        MarketTopic.FoodPrice => ("Giá thực phẩm đang tăng", "giá thực phẩm"),
        _ => null,
    };

    public static string? CategorySlug(MarketTopic topic) => topic switch
    {
        MarketTopic.Fuel => "transport",
        MarketTopic.Electricity or MarketTopic.Water => "bills",
        MarketTopic.FoodPrice => "food",
        _ => null,
    };

    public static string NewsBody(string subject, int usedPercent, string categoryName, string sourceName)
        => $"Có tin {subject} tăng. Ngân sách {categoryName} của bạn đã dùng {usedPercent}%, bạn nên để dư thêm cho kỳ tới. Nguồn: {sourceName}.";
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
            if (enabled && marketEvent.Direction == MarketDirection.Up)
            {
                await SendNewsAlertsAsync(marketEvent, settings, now, ct);
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

        var latest = waiting[^1];
        var reference = await _market.GetFuelPriceAsOfAsync(settings.ReferenceFuelKey, now.AddDays(-settings.CompareWindowDays), ct);
        if (enabled && reference is not null && reference.Id != latest.Id
            && MarketAlertRules.RoseEnough(reference.Region1PriceCents, latest.Region1PriceCents, settings.RiseThresholdPercent))
        {
            await ForEachActiveUserAsync(settings, now, (userId, today) => SendFuelAlertAsync(userId, today, latest, reference, settings, now, ct), ct);
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

    private async Task SendFuelAlertAsync(Guid userId, DateOnly today, FuelPrice latest, FuelPrice reference, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var prefix = KeyPrefix(MarketTopic.Fuel);
        if (await InCooldownAsync(userId, prefix, settings, now, ct))
        {
            return;
        }

        var fuelSpend = (await _reports.GetConfirmedDebitsAsync(userId, today.AddDays(-settings.SpendLookbackDays), today, ct))
            .Where(t => t.Category?.Slug == "transport" && MarketAlertRules.IsFuelPurchase(t))
            .ToList();
        if (fuelSpend.Count < settings.MinCategoryTransactions)
        {
            return;
        }

        var budget = await TightBudgetAsync(userId, fuelSpend[0].CategoryId, ct);
        if (budget is null)
        {
            return;
        }

        var increase = latest.Region1PriceCents - reference.Region1PriceCents;
        var extra = MarketAlertRules.ExtraCostCents(fuelSpend.Sum(t => t.AmountCents), settings.SpendLookbackDays, reference.Region1PriceCents, latest.Region1PriceCents);
        await _dispatcher.RaiseAsync(
            new ForecastAlertRequest(
                userId,
                ForecastAlertKind.MarketChange,
                $"{prefix}{latest.Id}",
                "Giá xăng đang tăng",
                MarketAlertRules.FuelBody(latest.FuelKey, increase, MarketAlertRules.UsedPercent(budget), extra, budget.CategoryName ?? "Di chuyển", reference.FirstSeenAt <= now.AddDays(-settings.CompareWindowDays) ? settings.CompareWindowDays : null),
                new Dictionary<string, string>
                {
                    ["topic"] = MarketEventText.Topic(MarketTopic.Fuel),
                    ["budgetId"] = budget.BudgetId.ToString(),
                    ["extraCostCents"] = extra.ToString(CultureInfo.InvariantCulture),
                },
                NotBefore: MarketAlertRules.NotBefore(now)),
            ct);
    }

    private async Task SendNewsAlertsAsync(MarketEvent marketEvent, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        if (MarketAlertRules.NewsTemplate(marketEvent.Topic) is not { } template || MarketAlertRules.CategorySlug(marketEvent.Topic) is not { } slug)
        {
            return;
        }

        var sources = await _market.ListEventSignalsAsync(marketEvent.Topic, marketEvent.Direction, marketEvent.FirstSeenAt.AddDays(-1), ct);
        var sourceName = sources.Count == 0 ? "tin tức" : sources[0].SourceName;
        var prefix = KeyPrefix(marketEvent.Topic);
        await ForEachActiveUserAsync(
            settings,
            now,
            async (userId, today) =>
            {
                if (await InCooldownAsync(userId, prefix, settings, now, ct))
                {
                    return;
                }

                var spend = (await _reports.GetConfirmedDebitsAsync(userId, today.AddDays(-settings.SpendLookbackDays), today, ct))
                    .Where(t => t.Category?.Slug == slug)
                    .ToList();
                if (spend.Count < settings.MinCategoryTransactions)
                {
                    return;
                }

                var budget = await TightBudgetAsync(userId, spend[0].CategoryId, ct);
                if (budget is null)
                {
                    return;
                }

                await _dispatcher.RaiseAsync(
                    new ForecastAlertRequest(
                        userId,
                        ForecastAlertKind.MarketChange,
                        $"{prefix}{marketEvent.Id}",
                        template.Title,
                        MarketAlertRules.NewsBody(template.Subject, MarketAlertRules.UsedPercent(budget), budget.CategoryName ?? slug, sourceName),
                        new Dictionary<string, string>
                        {
                            ["topic"] = MarketEventText.Topic(marketEvent.Topic),
                            ["budgetId"] = budget.BudgetId.ToString(),
                        },
                        NotBefore: MarketAlertRules.NotBefore(now)),
                    ct);
            },
            ct);
    }

    private async Task<BudgetForecastDto?> TightBudgetAsync(Guid userId, Guid? categoryId, CancellationToken ct)
    {
        var budget = (await _budgets.GetAsync(userId, ct)).FirstOrDefault(b => b.CategoryId == categoryId);
        if (budget is null)
        {
            return null;
        }

        var thresholds = await _thresholds.GetAsync(userId, ct);
        return MarketAlertRules.BudgetIsTight(budget, thresholds) ? budget : null;
    }

    private Task<bool> InCooldownAsync(Guid userId, string prefix, MarketDataSettings settings, DateTimeOffset now, CancellationToken ct)
        => _alerts.AnyWithKeyPrefixSinceAsync(userId, ForecastAlertKind.MarketChange, prefix, now.AddDays(-settings.AlertCooldownDays), ct);

    private static string KeyPrefix(MarketTopic topic) => $"market:{MarketEventText.Topic(topic)}:";
}
