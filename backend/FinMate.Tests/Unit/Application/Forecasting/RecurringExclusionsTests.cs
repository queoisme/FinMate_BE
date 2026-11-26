using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 92 (D5): khoản vừa hoàn thành vẫn loại các lần trả cũ khỏi thói quen chi tiêu và thu không đều.</summary>
public class RecurringExclusionsTests
{
    private static RecurringItem Completed(string name, long amount, DateOnly completedOn, RecurringDirection direction = RecurringDirection.Outflow) => new()
    {
        Id = Guid.NewGuid(), Direction = direction, Name = name, CounterpartyKey = FinMate.Application.Forecasting.CounterpartyKey.From(name),
        AmountCents = amount, Cadence = RecurringCadence.Monthly, NextDueDate = completedOn.AddDays(10), AnchorDay = 15, TotalOccurrences = 12,
        PrepaidOccurrences = 12, Status = RecurringStatus.Completed,
        CompletedAt = new DateTimeOffset(completedOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(7)),
    };

    private static readonly IReadOnlyList<ForecastTransaction> ThreeInstallments =
    [
        Debit(Today.AddDays(-70), 1_200_000, "FE Credit"),
        Debit(Today.AddDays(-40), 1_200_000, "FE Credit"),
        Debit(Today.AddDays(-10), 1_200_000, "FE Credit"),
        Debit(Today.AddDays(-5), 50_000, "Highlands"),
    ];

    [Fact]
    public void Baseline_AfterCompletion_DoesNotTurnOldInstallmentsIntoDailySpending()
    {
        var excluded = RecurringExclusions.With([], [Completed("FE Credit", 1_200_000, Today.AddDays(-9))], Today, Rules.HistoryWindowDays);

        var withExclusion = SpendingBaselineCalculator.Compute(ThreeInstallments, excluded, 0, Today, Rules);
        var withoutExclusion = SpendingBaselineCalculator.Compute(ThreeInstallments, [], 0, Today, Rules);

        excluded.Should().ContainSingle();
        withExclusion.ExpectedOn(Today.AddDays(1)).Should().BeLessThan(withoutExclusion.ExpectedOn(Today.AddDays(1)));
    }

    [Fact]
    public void OnlyItemsCompletedInsideTheWindow_AreKept()
    {
        var old = Completed("Home Credit", 900_000, Today.AddDays(-200));
        var recent = Completed("FE Credit", 1_200_000, Today.AddDays(-3));
        var active = Item("Netflix", 260_000, Today.AddDays(5));

        RecurringExclusions.With([active], [old, recent], Today, 90).Select(i => i.Name).Should().Equal("Netflix", "FE Credit");
    }

    [Fact]
    public void IrregularIncome_IgnoresAFinishedScholarship()
    {
        var scholarship = Completed("Học bổng ABC", 3_000_000, Today.AddDays(-20), RecurringDirection.Inflow);
        var history = Enumerable.Range(1, 4).Select(m => Credit(Today.AddMonths(-m), 3_000_000, "Học bổng ABC")).ToList();

        var excluded = RecurringExclusions.With([], [scholarship], Today, 200);

        IncomeEstimator.Irregular(history, excluded, Today, 3, Rules)!.ConservativeMonthlyCents.Should().Be(0);
        IncomeEstimator.Irregular(history, [], Today, 3, Rules)!.ConservativeMonthlyCents.Should().Be(3_000_000);
    }
}
