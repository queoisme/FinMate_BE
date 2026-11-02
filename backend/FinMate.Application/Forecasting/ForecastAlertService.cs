using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public sealed record ForecastAlertDto(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    JsonElement Data,
    string Status,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? SentAt,
    bool MarkedInaccurate,
    DateTimeOffset CreatedAt);

public interface IForecastAlertService
{
    /// <summary>Kiểm tra sau khi user có thay đổi (giao dịch mới…): số dư thấp và vượt ngân sách sớm.</summary>
    Task CheckUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Lượt kiểm tra hằng ngày: như <see cref="CheckUserAsync"/> cộng nhắc hóa đơn, tạm ngừng khoản quá hạn lâu, thu nhập trễ.</summary>
    Task RunDailyAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ForecastAlertDto>> ListAsync(Guid userId, int limit, CancellationToken ct = default);

    /// <summary>User báo cảnh báo không chính xác (sơ đồ trang 1) — hiện chỉ nhận <c>"inaccurate"</c>. Cảnh báo số dư thấp bị tắt một thời gian.</summary>
    Task<ForecastAlertDto> SetFeedbackAsync(Guid userId, Guid alertId, string? feedback, CancellationToken ct = default);
}

public class ForecastAlertService : IForecastAlertService
{
    private const int MaxListLimit = 100;
    private const string InaccurateFeedback = "inaccurate";

    private readonly IForecastService _forecast;
    private readonly IBudgetForecastService _budgetForecast;
    private readonly IRecurringItemRepository _items;
    private readonly IForecastDataRepository _data;
    private readonly IUserRepository _users;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly IForecastAlertRepository _alerts;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;

    public ForecastAlertService(
        IForecastService forecast,
        IBudgetForecastService budgetForecast,
        IRecurringItemRepository items,
        IForecastDataRepository data,
        IUserRepository users,
        IForecastAlertDispatcher dispatcher,
        IForecastAlertRepository alerts,
        ISystemConfigProvider config,
        ICacheService cache)
    {
        _forecast = forecast;
        _budgetForecast = budgetForecast;
        _items = items;
        _data = data;
        _users = users;
        _dispatcher = dispatcher;
        _alerts = alerts;
        _config = config;
        _cache = cache;
    }

    public async Task CheckUserAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        await CheckLowBalanceAsync(userId, settings, ct);
        await CheckBudgetsAsync(userId, settings, ct);
    }

    public async Task RunDailyAsync(Guid userId, CancellationToken ct = default)
    {
        await CheckUserAsync(userId, ct);
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var rules = ForecastRules.From(settings);
        var today = VietnamTime.Today();
        var active = await _items.ListAsync(userId, RecurringStatus.Active, ct);
        if (active.Count == 0)
        {
            return;
        }

        var views = active.Select(i => i.ToView()).ToList();
        var recent = await _data.GetTransactionsAsync(userId, today.AddDays(-rules.HistoryWindowDays), today, ct);
        await RemindBillsAsync(userId, active, views, recent, today, settings, rules, ct);
        await WarnLateIncomeAsync(userId, views, recent, today, settings, rules, ct);
    }

    public async Task<IReadOnlyList<ForecastAlertDto>> ListAsync(Guid userId, int limit, CancellationToken ct = default)
        => (await _alerts.ListForUserAsync(userId, Math.Clamp(limit, 1, MaxListLimit), ct)).Select(ToDto).ToList();

    public async Task<ForecastAlertDto> SetFeedbackAsync(Guid userId, Guid alertId, string? feedback, CancellationToken ct = default)
    {
        if (!string.Equals(feedback, InaccurateFeedback, StringComparison.OrdinalIgnoreCase))
        {
            throw new FluentValidation.ValidationException(
                [new FluentValidation.Results.ValidationFailure(nameof(feedback), "Phản hồi chỉ nhận giá trị 'inaccurate'.")]);
        }

        var alert = await _alerts.GetAsync(alertId, userId, ct) ?? throw new NotFoundException("ForecastAlert", alertId);
        if (alert.MarkedInaccurate != true)
        {
            alert.MarkedInaccurate = true;
            alert.FeedbackAt = DateTimeOffset.UtcNow;
            await _alerts.UpdateAsync(alert, ct);
        }

        return ToDto(alert);
    }

    private async Task CheckLowBalanceAsync(Guid userId, Admin.SystemConfig.ForecastSettings settings, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user?.LowBalanceThresholdCents is not { } threshold)
        {
            return;
        }

        var forecast = await _forecast.GetCashflowAsync(userId, null, VietnamTime.Today().AddDays(settings.LowBalanceHorizonDays), ct);
        if (LowBalanceRule.Evaluate(forecast, threshold) is not { } warning)
        {
            return;
        }

        var cause = warning.Causes.FirstOrDefault()?.Label;
        await _dispatcher.RaiseAsync(new ForecastAlertRequest(
            userId,
            ForecastAlertKind.LowBalance,
            $"low_balance:{ForecastText.Iso(warning.ShortfallDate)}",
            "Số dư sắp xuống dưới mức bạn đặt",
            $"Khoảng {ForecastText.Day(warning.ShortfallDate)} số dư dự kiến còn {ForecastText.Money(warning.BalanceAtShortfallCents)} "
            + $"(ngưỡng {ForecastText.Money(threshold)}). Nên chi tối đa {ForecastText.Money(warning.MaxDailySpendCents)}/ngày"
            + (cause is null ? "." : $". Khoản lớn nhất: {cause}."),
            new Dictionary<string, string>
            {
                ["shortfallDate"] = ForecastText.Iso(warning.ShortfallDate),
                ["balanceAtShortfallCents"] = warning.BalanceAtShortfallCents.ToString(),
                ["maxDailySpendCents"] = warning.MaxDailySpendCents.ToString(),
            },
            HonorQuietHours: true), ct);
    }

    private async Task CheckBudgetsAsync(Guid userId, Admin.SystemConfig.ForecastSettings settings, CancellationToken ct)
    {
        var periodKey = VietnamTime.Today().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var budget in await _budgetForecast.GetAsync(userId, ct))
        {
            if (!BudgetRiskCalculator.ShouldWarn(budget, settings.BudgetRiskMinDaysRemaining))
            {
                continue;
            }

            var scope = budget.CategoryName ?? "tổng chi tiêu";
            await _dispatcher.RaiseAsync(new ForecastAlertRequest(
                userId,
                ForecastAlertKind.BudgetRisk,
                $"budget_risk:{budget.BudgetId}:{periodKey}",
                $"Có thể vượt ngân sách {scope}",
                $"Cứ đà này bạn sẽ chi khoảng {ForecastText.Money(budget.ProjectedCents)} / hạn mức {ForecastText.Money(budget.LimitCents)}. "
                + $"Giữ dưới {ForecastText.Money(budget.MaxDailySpendCents)}/ngày để không vượt.",
                new Dictionary<string, string>
                {
                    ["budgetId"] = budget.BudgetId.ToString(),
                    ["projectedCents"] = budget.ProjectedCents.ToString(),
                    ["maxDailySpendCents"] = budget.MaxDailySpendCents.ToString(),
                }), ct);
        }
    }

    private async Task RemindBillsAsync(
        Guid userId,
        IReadOnlyList<RecurringItem> active,
        IReadOnlyList<RecurringItemView> views,
        IReadOnlyList<ForecastTransaction> recent,
        DateOnly today,
        Admin.SystemConfig.ForecastSettings settings,
        ForecastRules rules,
        CancellationToken ct)
    {
        var paused = false;
        foreach (var reminder in BillReminderRule.Evaluate(views, recent, today, settings.BillReminderDaysBefore, rules))
        {
            var item = reminder.Item;
            var data = new Dictionary<string, string> { ["recurringItemId"] = item.Id.ToString(), ["dueDate"] = ForecastText.Iso(reminder.DueDate) };
            if (reminder.Kind == BillReminderKind.DueSoon)
            {
                await _dispatcher.RaiseAsync(new ForecastAlertRequest(
                    userId, ForecastAlertKind.BillDue, $"bill_due:{item.Id}:{ForecastText.Iso(reminder.DueDate)}",
                    $"Sắp đến hạn {item.Name}",
                    $"{ForecastText.Money(item.AmountCents)} đến hạn ngày {ForecastText.Day(reminder.DueDate)}.", data), ct);
                continue;
            }

            // Quá hạn lâu: tạm ngừng theo dõi (không trừ vào dự báo nữa) và hỏi user còn dùng không — trả lời bằng resume hoặc DELETE.
            var entity = active.First(a => a.Id == item.Id);
            entity.Status = RecurringStatus.Paused;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _items.UpdateAsync(entity, ct);
            paused = true;

            await _dispatcher.RaiseAsync(new ForecastAlertRequest(
                userId, ForecastAlertKind.BillStatusCheck, $"bill_status_check:{item.Id}:{ForecastText.Iso(reminder.DueDate)}",
                $"{item.Name} chưa thanh toán",
                $"Đã quá hạn {reminder.DaysOverdue} ngày mà chưa thấy khoản {ForecastText.Money(item.AmountCents)}. Bạn còn dùng khoản này không?",
                data), ct);
        }

        if (paused)
        {
            await _cache.RemoveAsync(CacheKeys.ForecastBaseline(userId, today), ct);
        }
    }

    private async Task WarnLateIncomeAsync(
        Guid userId,
        IReadOnlyList<RecurringItemView> views,
        IReadOnlyList<ForecastTransaction> recent,
        DateOnly today,
        Admin.SystemConfig.ForecastSettings settings,
        ForecastRules rules,
        CancellationToken ct)
    {
        foreach (var late in IncomeEstimator.Late(views, recent, today, settings.IncomeLateDays, rules))
        {
            await _dispatcher.RaiseAsync(new ForecastAlertRequest(
                userId, ForecastAlertKind.IncomeLate, $"income_late:{late.RecurringItemId}:{ForecastText.Iso(late.ExpectedDate)}",
                $"Chưa nhận được {late.Name}",
                $"Dự kiến ngày {ForecastText.Day(late.ExpectedDate)} ({ForecastText.Money(late.AmountCents)}). Thu nhập bị dời hay đã thay đổi?",
                new Dictionary<string, string>
                {
                    ["recurringItemId"] = late.RecurringItemId.ToString(),
                    ["expectedDate"] = ForecastText.Iso(late.ExpectedDate),
                }), ct);
        }
    }

    private static JsonElement ParseData(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ForecastAlertDto ToDto(ForecastAlert a) => new(
        a.Id,
        ForecastAlertKinds.Text(a.Kind),
        a.Title,
        a.Body,
        ParseData(a.Data),
        a.Status.ToString().ToLowerInvariant(),
        a.ScheduledFor,
        a.SentAt,
        a.MarkedInaccurate == true,
        a.CreatedAt);
}
