using System.Globalization;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Forecasting;

public sealed record CategoryForecastItemDto(
    Guid? CategoryId,
    string CategoryName,
    long ProjectedCents,
    string Basis,
    string Confidence,
    long PreviousMonthCents,
    long? CurrentLimitCents,
    int? ChangePercent,
    bool ChangedSignificantly,
    long? SuggestedLimitCents);

/// <param name="SeasonalAdjusted">Luôn false: chưa điều chỉnh mùa vụ (cần trên một năm dữ liệu).</param>
public sealed record CategoryForecastDto(
    string Month,
    IReadOnlyList<string> AvailableMonths,
    bool EnoughHistory,
    int MonthsWithData,
    bool SeasonalAdjusted,
    long ProjectedTotalCents,
    long PreviousMonthTotalCents,
    IReadOnlyList<CategoryForecastItemDto> Items);

public sealed record CategoryBudgetItem(Guid CategoryId, long LimitCents);

/// <param name="Effective">"next_period" = danh mục đã có ngân sách, hạn mức mới áp từ tháng sau; "now" = ngân sách mới tạo, áp ngay.</param>
public sealed record AppliedCategoryBudgetDto(Guid BudgetId, Guid CategoryId, long LimitCents, string Effective);

public interface ICategoryForecastService
{
    /// <param name="month">"yyyy-MM"; null = tháng tới.</param>
    Task<CategoryForecastDto> GetAsync(Guid userId, string? month, CancellationToken ct = default);

    /// <summary>"Dùng làm ngân sách kỳ tới": lưu hạn mức (user đã xem và chỉnh) cho từng danh mục.</summary>
    Task<IReadOnlyList<AppliedCategoryBudgetDto>> ApplyAsync(Guid userId, IReadOnlyList<CategoryBudgetItem> items, CancellationToken ct = default);
}

public class CategoryForecastService : ICategoryForecastService
{
    public const string EffectiveNextPeriod = "next_period";
    public const string EffectiveNow = "now";
    private const string MonthFormat = "yyyy-MM";
    private const string UncategorizedName = "Chưa phân loại";
    private const int SelectableMonths = 2;
    private const int MaxApplyItems = 50;
    private const long MaxLimitCents = 100_000_000_000;

    private readonly IForecastDataRepository _data;
    private readonly IAnomalyRepository _anomalies;
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly ICreateBudgetCommandHandler _createBudget;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;

    public CategoryForecastService(
        IForecastDataRepository data,
        IAnomalyRepository anomalies,
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        ICreateBudgetCommandHandler createBudget,
        ISystemConfigProvider config,
        ICacheService cache)
    {
        _data = data;
        _anomalies = anomalies;
        _budgets = budgets;
        _categories = categories;
        _transactions = transactions;
        _createBudget = createBudget;
        _config = config;
        _cache = cache;
    }

    public async Task<CategoryForecastDto> GetAsync(Guid userId, string? month, CancellationToken ct = default)
    {
        var today = VietnamTime.Today();
        var available = Enumerable.Range(1, SelectableMonths).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i)).ToList();
        var target = ParseMonth(month, available);

        var rules = CategoryForecastRules.From((await _config.GetAsync(ct)).Document.Forecast);
        var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-rules.Months);
        var history = await _data.GetTransactionsAsync(userId, windowStart, today.AddDays(-1), ct);
        var excluded = await _anomalies.GetNotNormalTransactionIdsAsync(userId, ct);
        var budgets = await _budgets.GetListForUserAsync(userId, ct);
        var limits = budgets.Where(b => b.CategoryId is not null).ToDictionary(b => b.CategoryId!.Value, b => b.LimitCents);
        var result = CategoryForecastCalculator.Compute(history, excluded, limits, today, target, rules);

        var names = (await _categories.GetListForUserAsync(userId, ct)).ToDictionary(c => c.Id, c => c.Name);
        var items = result.Items.Select(i => ToDto(i, names)).ToList();
        return new CategoryForecastDto(
            target.ToString(MonthFormat, CultureInfo.InvariantCulture),
            available.Select(m => m.ToString(MonthFormat, CultureInfo.InvariantCulture)).ToList(),
            result.EnoughHistory,
            result.MonthsWithData,
            SeasonalAdjusted: false,
            items.Sum(i => i.ProjectedCents),
            items.Sum(i => i.PreviousMonthCents),
            items);
    }

    public async Task<IReadOnlyList<AppliedCategoryBudgetDto>> ApplyAsync(
        Guid userId, IReadOnlyList<CategoryBudgetItem> items, CancellationToken ct = default)
    {
        Validate(items);
        foreach (var item in items)
        {
            var category = await _categories.GetByIdAsync(item.CategoryId, ct);
            if (category is null || (category.UserId is not null && category.UserId != userId))
            {
                throw new NotFoundException("Category", item.CategoryId);
            }
        }

        var existing = (await _budgets.GetListForUserAsync(userId, ct))
            .Where(b => b.CategoryId is not null && b.PeriodType == BudgetPeriodType.Monthly)
            .ToDictionary(b => b.CategoryId!.Value);
        var applied = new List<AppliedCategoryBudgetDto>();
        foreach (var item in items)
        {
            applied.Add(existing.TryGetValue(item.CategoryId, out var budget)
                ? await ScheduleForNextPeriodAsync(userId, budget, item.LimitCents, ct)
                : await CreateNowAsync(userId, item, ct));
        }

        var (year, month) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        await _cache.RemoveAsync(CacheKeys.BudgetSummary(userId, year, month), ct);
        return applied;
    }

    /// <summary>
    /// Chốt snapshot tháng này (giữ hạn mức cũ) TRƯỚC khi đổi <c>budgets.limit_cents</c>: kỳ được tạo lười theo hạn mức của budget,
    /// nên tháng này chưa có kỳ mà đổi luôn thì tháng này cũng nhận hạn mức mới.
    /// </summary>
    private async Task<AppliedCategoryBudgetDto> ScheduleForNextPeriodAsync(Guid userId, Budget budget, long limitCents, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var (periodStart, periodEnd) = VietnamTime.MonthRange(now);
        if (await _budgets.GetPeriodAsync(budget.Id, periodStart, ct) is null)
        {
            var spent = await _transactions.SumConfirmedSpendAsync(userId, budget.CategoryId, periodStart, periodEnd, ct);
            _budgets.AddPeriod(new BudgetPeriod
            {
                Id = Guid.NewGuid(),
                BudgetId = budget.Id,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                LimitCents = budget.LimitCents,
                SpentCents = spent,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        budget.LimitCents = limitCents;
        budget.UpdatedAt = now;
        await _budgets.UpdateAsync(budget, ct);
        return new AppliedCategoryBudgetDto(budget.Id, budget.CategoryId!.Value, limitCents, EffectiveNextPeriod);
    }

    private async Task<AppliedCategoryBudgetDto> CreateNowAsync(Guid userId, CategoryBudgetItem item, CancellationToken ct)
    {
        BudgetDto created = await _createBudget.HandleAsync(new CreateBudgetCommand(userId, item.CategoryId, item.LimitCents), ct);
        return new AppliedCategoryBudgetDto(created.Id, item.CategoryId, item.LimitCents, EffectiveNow);
    }

    private static void Validate(IReadOnlyList<CategoryBudgetItem>? items)
    {
        var failures = new List<ValidationFailure>();
        if (items is null || items.Count is 0 or > MaxApplyItems)
        {
            failures.Add(new ValidationFailure(nameof(items), $"Cần từ 1 đến {MaxApplyItems} danh mục."));
        }
        else
        {
            if (items.Any(i => i.LimitCents is <= 0 or > MaxLimitCents))
            {
                failures.Add(new ValidationFailure(nameof(CategoryBudgetItem.LimitCents), "Hạn mức phải lớn hơn 0."));
            }

            if (items.Select(i => i.CategoryId).Distinct().Count() != items.Count)
            {
                failures.Add(new ValidationFailure(nameof(CategoryBudgetItem.CategoryId), "Mỗi danh mục chỉ xuất hiện một lần."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static DateOnly ParseMonth(string? month, IReadOnlyList<DateOnly> available)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            return available[0];
        }

        if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && available.Contains(parsed))
        {
            return parsed;
        }

        throw new ValidationException(
            [new ValidationFailure(nameof(month), $"Chỉ dự báo được các tháng: {string.Join(", ", available.Select(m => m.ToString(MonthFormat, CultureInfo.InvariantCulture)))}.")]);
    }

    private static CategoryForecastItemDto ToDto(CategoryForecastItem i, IReadOnlyDictionary<Guid, string> names) => new(
        i.CategoryId,
        i.CategoryId is { } id ? names.GetValueOrDefault(id, UncategorizedName) : UncategorizedName,
        i.ProjectedCents,
        i.Basis,
        i.Confidence.ToString().ToLowerInvariant(),
        i.PreviousMonthCents,
        i.CurrentLimitCents,
        i.ChangePercent,
        i.ChangedSignificantly,
        i.SuggestedLimitCents);
}
