using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class CategoryForecastCalculatorTests
{
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Fun = Guid.NewGuid();
    private static readonly DateOnly NextMonth = new(2026, 12, 1);
    private static readonly CategoryForecastRules CategoryRules = CategoryForecastRules.From(new ForecastSettings());

    private static ForecastTransaction Spend(DateOnly date, long amount, Guid? category, bool excluded = false)
        => new(Guid.NewGuid(), Account, category, amount, TransactionType.Debit,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), VietnamTime.Offset), date, null, null, excluded);

    private static CategoryForecastResult Compute(
        IReadOnlyList<ForecastTransaction> history, IReadOnlySet<Guid>? excluded = null, IReadOnlyDictionary<Guid, long>? limits = null)
        => CategoryForecastCalculator.Compute(history, excluded ?? new HashSet<Guid>(), limits ?? new Dictionary<Guid, long>(), Today, NextMonth, CategoryRules);

    [Fact]
    public void ThreeFullMonths_UseARecencyWeightedTrend()
    {
        // Tháng 8 → 10: 3tr, 2tr, 1tr (tháng 10 gần nhất). (1·3 + 2·2 + 3·1) / 6 = 1,666,667.
        var history = new[]
        {
            Spend(new(2026, 8, 10), 3_000_000, Food),
            Spend(new(2026, 9, 10), 2_000_000, Food),
            Spend(new(2026, 10, 10), 1_000_000, Food),
        };

        var item = Compute(history).Items.Single();

        item.ProjectedCents.Should().Be(1_666_667);
        item.Basis.Should().Be(SpendingBaseline.BasisHistory);
        item.Confidence.Should().Be(ForecastConfidence.Normal);
        item.PreviousMonthCents.Should().Be(1_000_000);
        item.ChangePercent.Should().Be(67);
        item.ChangedSignificantly.Should().BeTrue();
        item.SuggestedLimitCents.Should().Be(1_670_000);
    }

    [Fact]
    public void MonthsBeforeTheUserStarted_DoNotCountAsZeroSpending()
    {
        // Dùng app từ tháng 9: chỉ tháng 9, 10 được tính, trọng số 1 và 2.
        var history = new[] { Spend(new(2026, 9, 5), 900_000, Food), Spend(new(2026, 10, 5), 1_200_000, Food) };

        Compute(history).Items.Single().ProjectedCents.Should().Be(1_100_000);
    }

    [Fact]
    public void NotEnoughHistory_FallsBackToTheBudgetWithLowConfidence()
    {
        var history = new[] { Spend(new(2026, 10, 20), 500_000, Food) };

        var result = Compute(history, limits: new Dictionary<Guid, long> { [Food] = 2_000_000 });

        result.EnoughHistory.Should().BeFalse();
        var item = result.Items.Single();
        item.ProjectedCents.Should().Be(2_000_000);
        item.Basis.Should().Be(SpendingBaseline.BasisBudget);
        item.Confidence.Should().Be(ForecastConfidence.Low);
    }

    [Fact]
    public void NotEnoughHistoryAndNoBudget_ScalesTheDailyAverageToTheTargetMonth()
    {
        // Từ 20/10 tới hôm nay (02/11) là 13 ngày, chi 1,3tr → 100k/ngày × 31 ngày tháng 12.
        var history = new[] { Spend(new(2026, 10, 20), 1_300_000, Fun) };

        var item = Compute(history).Items.Single();

        item.ProjectedCents.Should().Be(3_100_000);
        item.Basis.Should().Be(SpendingBaseline.BasisAverage);
    }

    [Fact]
    public void OneOffsAndUnconfirmedAnomalies_AreLeftOut()
    {
        var anomaly = Spend(new(2026, 10, 15), 9_000_000, Food);
        var history = new[]
        {
            Spend(new(2026, 9, 10), 1_000_000, Food),
            Spend(new(2026, 10, 10), 1_000_000, Food),
            Spend(new(2026, 10, 12), 5_000_000, Food, excluded: true),
            anomaly,
        };

        Compute(history, new HashSet<Guid> { anomaly.Id }).Items.Single().ProjectedCents.Should().Be(1_000_000);
    }

    [Fact]
    public void SmallChange_IsNotFlagged()
    {
        var history = new[] { Spend(new(2026, 9, 10), 1_000_000, Food), Spend(new(2026, 10, 10), 1_100_000, Food) };

        var item = Compute(history).Items.Single();

        item.ChangePercent.Should().BeLessThan(20);
        item.ChangedSignificantly.Should().BeFalse();
    }

    [Fact]
    public void Uncategorized_IsForecastButGetsNoSuggestedLimit()
    {
        var history = new[] { Spend(new(2026, 9, 10), 400_000, null), Spend(new(2026, 10, 10), 400_000, null) };

        var item = Compute(history).Items.Single();

        item.CategoryId.Should().BeNull();
        item.SuggestedLimitCents.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10_000)]
    [InlineData(10_000, 10_000)]
    [InlineData(10_001, 20_000)]
    public void RoundUp_NeverSuggestsLessThanTheForecast(long amount, long expected)
        => CategoryForecastCalculator.RoundUp(amount, 10_000).Should().Be(expected);
}
