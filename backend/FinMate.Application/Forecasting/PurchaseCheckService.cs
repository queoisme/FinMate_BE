using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <param name="CategorySlug">Slug danh mục hệ thống (như chat trả về); chỉ dùng khi không có <paramref name="CategoryId"/>.</param>
public sealed record PurchaseCheckCommand(
    Guid UserId, long AmountCents, DateOnly? PurchaseDate, string? Item, Guid? CategoryId, Guid? AccountId, int? HorizonDays, string? CategorySlug = null);

public sealed record PurchaseCheckDto(
    string Status,
    string Verdict,
    string Summary,
    long AmountCents,
    DateOnly PurchaseDate,
    string? Item,
    PurchasePeriodDto Period,
    PurchasePointDto? LowestBefore,
    PurchasePointDto? LowestAfter,
    long? ConservativeLowestAfterCents,
    long BufferCents,
    long ShortfallCents,
    PurchaseBudgetImpactDto Budget,
    PurchasePlanImpactDto Plan,
    long? MaxAffordableCents,
    DateOnly? EarliestSafeDate,
    IReadOnlyList<PurchaseLineDto> Lines,
    IReadOnlyList<PurchaseWarningDto> Warnings,
    string Confidence,
    DateTimeOffset? BalanceAsOf,
    DateTimeOffset ComputedAt);

public interface IPurchaseCheckService
{
    /// <summary>Đánh giá "mua khoản này vào ngày đó có ổn không". Chỉ đọc: không tạo giao dịch, không lưu gì.</summary>
    Task<PurchaseCheckDto> CheckAsync(PurchaseCheckCommand command, CancellationToken ct = default);
}

/// <summary>
/// Dựng đầu vào cho <see cref="PurchaseVerdict"/> từ dự báo dòng tiền có sẵn: chạy cùng một engine hai lần (không mua / có khoản mua giả định)
/// để so điểm số dư thấp nhất trong kỳ tới kỳ thu nhập kế tiếp. Số liệu đều do code tính; không có AI trong đường này.
/// </summary>
public class PurchaseCheckService : IPurchaseCheckService
{
    private const long MaxAmountCents = 100_000_000_000;
    private const int ItemMaxLength = 60;
    private const string DefaultLabel = "Khoản mua";

    private readonly IForecastService _forecast;
    private readonly IFinancialAccountRepository _accounts;
    private readonly IRecurringItemRepository _items;
    private readonly IBudgetForecastService _budgets;
    private readonly IGoalPlanningService _goals;
    private readonly IUserRepository _users;
    private readonly ICategoryRepository _categories;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public PurchaseCheckService(
        IForecastService forecast,
        IFinancialAccountRepository accounts,
        IRecurringItemRepository items,
        IBudgetForecastService budgets,
        IGoalPlanningService goals,
        IUserRepository users,
        ICategoryRepository categories,
        ISystemConfigProvider config)
        : this(forecast, accounts, items, budgets, goals, users, categories, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal PurchaseCheckService(
        IForecastService forecast,
        IFinancialAccountRepository accounts,
        IRecurringItemRepository items,
        IBudgetForecastService budgets,
        IGoalPlanningService goals,
        IUserRepository users,
        ICategoryRepository categories,
        ISystemConfigProvider config,
        Func<DateTimeOffset> now)
    {
        _forecast = forecast;
        _accounts = accounts;
        _items = items;
        _budgets = budgets;
        _goals = goals;
        _users = users;
        _categories = categories;
        _config = config;
        _now = now;
    }

    public async Task<PurchaseCheckDto> CheckAsync(PurchaseCheckCommand command, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var now = _now();
        var today = VietnamTime.DateOf(now);
        var purchaseDate = Validate(command, settings, today);
        var user = await _users.GetByIdAsync(command.UserId, ct) ?? throw new NotFoundException("User", command.UserId);

        // Cùng một cửa sổ cho cả hai lần chạy để hai đường số dư thẳng hàng theo ngày.
        var windowEnd = today.AddDays(settings.MaxHorizonDays);
        var label = string.IsNullOrWhiteSpace(command.Item) ? DefaultLabel : command.Item.Trim();
        var baseline = await _forecast.GetCashflowAsync(command.UserId, command.AccountId, windowEnd, ct);
        var withPurchase = await _forecast.RunScenarioAsync(
            new CashflowScenarioCommand(command.UserId, command.AccountId, windowEnd, [new ScenarioItem(purchaseDate, command.AmountCents, RecurringDirection.Outflow, label)]), ct);

        var accounts = await AccountsAsync(command, ct);
        var days = baseline.Days.Zip(withPurchase.Days, (b, a) => new PurchaseDay(b.Date, b.ExpectedBalanceCents, a.ExpectedBalanceCents, a.LowBalanceCents)).ToList();
        var incomeDates = baseline.Days.Where(d => d.Events.Any(e => e.Kind == CashflowEventKinds.RecurringInflow)).Select(d => d.Date).ToList();
        var horizon = command.HorizonDays ?? settings.PurchaseDefaultHorizonDays;
        var period = PurchaseVerdict.ResolvePeriod(today, purchaseDate, incomeDates, command.HorizonDays, settings.PurchaseDefaultHorizonDays, windowEnd);

        var input = new PurchaseVerdictInput(
            today, purchaseDate, command.AmountCents, user.LowBalanceThresholdCents ?? 0, period, days, incomeDates, horizon,
            QualityOf(command, baseline, period, accounts, today, settings),
            await BudgetImpactAsync(command, purchaseDate, today, ct),
            await PlanImpactAsync(command, purchaseDate, today, ct));
        var result = PurchaseVerdict.Evaluate(input);

        return new PurchaseCheckDto(
            result.Status, result.Verdict, PurchaseVerdict.Summary(input, result, command.Item), command.AmountCents, purchaseDate, command.Item?.Trim(),
            period, result.LowestBefore, result.LowestAfter, result.ConservativeLowestAfterCents, input.BufferCents, result.ShortfallCents,
            input.Budget, input.Plan, result.MaxAffordableCents, result.EarliestSafeDate,
            await LinesAsync(command.UserId, withPurchase, period, today, ct), result.Warnings, baseline.Confidence,
            accounts.Count == 0 ? null : accounts.Max(a => a.UpdatedAt), now);
    }

    private static DateOnly Validate(PurchaseCheckCommand command, ForecastSettings settings, DateOnly today)
    {
        if (command.AmountCents <= 0 || command.AmountCents > MaxAmountCents)
        {
            throw Invalid("Giá phải lớn hơn 0 và không quá 100 tỷ đồng.");
        }

        if (command.Item is { } item && item.Trim().Length > ItemMaxLength)
        {
            throw Invalid($"Tên món tối đa {ItemMaxLength} ký tự.");
        }

        var date = command.PurchaseDate ?? today;
        if (date < today || date > today.AddDays(settings.MaxHorizonDays))
        {
            throw Invalid($"Ngày mua phải từ hôm nay tới tối đa {settings.MaxHorizonDays} ngày sau.");
        }

        if (command.HorizonDays is { } horizon && (horizon < 1 || horizon > settings.MaxHorizonDays))
        {
            throw Invalid($"Mốc thời gian phải từ 1 đến {settings.MaxHorizonDays} ngày.");
        }

        return date;
    }

    private static BusinessRuleException Invalid(string message) => new(ForecastErrorCodes.PurchaseInvalid, message);

    /// <summary>Cùng phạm vi ví với dự báo: một ví cụ thể của chính user, hoặc mọi ví đang được tính vào dự báo.</summary>
    private async Task<IReadOnlyList<FinancialAccount>> AccountsAsync(PurchaseCheckCommand command, CancellationToken ct)
    {
        if (command.AccountId is { } id)
        {
            return await _accounts.GetByIdAsync(id, command.UserId, ct) is { } one ? [one] : [];
        }

        return (await _accounts.GetListByUserAsync(command.UserId, ct)).Where(a => a.IncludeInForecast).ToList();
    }

    private static PurchaseQuality QualityOf(
        PurchaseCheckCommand command, CashflowForecastDto baseline, PurchasePeriodDto period,
        IReadOnlyList<FinancialAccount> accounts, DateOnly today, ForecastSettings settings)
    {
        // Chưa có cột "số dư cập nhật lúc nào": dùng lần cập nhật gần nhất của các ví trong phạm vi làm xấp xỉ.
        int? age = accounts.Count == 0 ? null : today.DayNumber - VietnamTime.DateOf(accounts.Max(a => a.UpdatedAt)).DayNumber;
        return new PurchaseQuality(
            accounts.Count > 0,
            baseline.Confidence == "low",
            period.Basis == PurchasePeriodBases.NextIncome,
            command.HorizonDays is not null,
            age,
            settings.PurchaseStaleBalanceDays);
    }

    /// <summary>Ngân sách chỉ là ràng buộc kế hoạch (không trừ thêm vào tiền mặt). Không có ngân sách tương ứng thì "chưa rõ", không phải 0.</summary>
    private async Task<PurchaseBudgetImpactDto> BudgetImpactAsync(PurchaseCheckCommand command, DateOnly purchaseDate, DateOnly today, CancellationToken ct)
    {
        var unknown = new PurchaseBudgetImpactDto(PurchaseImpactStatuses.Unknown, null, null, null, null);
        if (!SameMonth(purchaseDate, today))
        {
            return unknown;
        }

        var budgets = await _budgets.GetAsync(command.UserId, ct);
        var categoryId = command.CategoryId
            ?? (string.IsNullOrWhiteSpace(command.CategorySlug) ? null : (await _categories.GetSystemBySlugAsync(command.CategorySlug.Trim(), ct))?.Id);
        var match = (categoryId is { } id ? budgets.FirstOrDefault(b => b.CategoryId == id) : null)
            ?? budgets.FirstOrDefault(b => b.CategoryId is null);
        if (match is null)
        {
            return unknown;
        }

        var after = match.ProjectedCents + command.AmountCents;
        return new PurchaseBudgetImpactDto(
            after > match.LimitCents ? PurchaseImpactStatuses.Over : PurchaseImpactStatuses.Ok, match.CategoryName, match.LimitCents, match.ProjectedCents, after);
    }

    /// <summary>Số còn lại của kế hoạch tháng (<c>/me/spendable</c>, đã tính <c>GoalMode</c>) sau khoản mua. Chưa khai thu nhập thì "chưa rõ".</summary>
    private async Task<PurchasePlanImpactDto> PlanImpactAsync(PurchaseCheckCommand command, DateOnly purchaseDate, DateOnly today, CancellationToken ct)
    {
        var unknown = new PurchasePlanImpactDto(PurchaseImpactStatuses.Unknown, null, null);
        if (!SameMonth(purchaseDate, today))
        {
            return unknown;
        }

        var plan = await _goals.GetSpendableAsync(command.UserId, ct);
        if (plan.SpendableCents is not { } spendable)
        {
            return unknown;
        }

        var after = spendable - command.AmountCents;
        return new PurchasePlanImpactDto(after < 0 ? PurchaseImpactStatuses.Over : PurchaseImpactStatuses.Ok, spendable, after);
    }

    private static bool SameMonth(DateOnly a, DateOnly b) => a.Year == b.Year && a.Month == b.Month;

    /// <summary>Bảng giải thích: số dư đầu kỳ, từng khoản thu/chi theo ngày, chi hằng ngày ước tính và khoản mua; mỗi dòng ghi nguồn.</summary>
    private async Task<IReadOnlyList<PurchaseLineDto>> LinesAsync(
        Guid userId, CashflowForecastDto forecast, PurchasePeriodDto period, DateOnly today, CancellationToken ct)
    {
        var manual = (await _items.ListAsync(userId, RecurringStatus.Active, ct)).Where(i => i.Source == RecurringSource.Manual).Select(i => i.Id).ToHashSet();
        var inPeriod = forecast.Days.Where(d => d.Date <= period.To).ToList();
        var lines = new List<PurchaseLineDto> { new(today, "Số dư hiện tại", forecast.StartBalanceCents, PurchaseSources.Confirmed) };
        foreach (var day in inPeriod)
        {
            lines.AddRange(day.Events.Select(e => new PurchaseLineDto(day.Date, e.Label, e.AmountCents, SourceOf(e, manual))));
        }

        var flexible = inPeriod.Sum(d => d.FlexibleSpendCents);
        if (flexible > 0)
        {
            lines.Add(new PurchaseLineDto(null, "Chi tiêu hằng ngày (ước tính)", -flexible, PurchaseSources.Estimated));
        }

        return lines;
    }

    private static string SourceOf(CashflowEventDto e, IReadOnlySet<Guid> manual)
        => e.Kind == CashflowEventKinds.Scenario || (e.RecurringItemId is { } id && manual.Contains(id))
            ? PurchaseSources.UserEntered
            : PurchaseSources.Confirmed;
}
