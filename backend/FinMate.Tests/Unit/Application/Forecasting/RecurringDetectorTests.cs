using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class RecurringDetectorTests
{
    private static readonly HashSet<(RecurringDirection, string)> None = [];

    [Fact]
    public void ThreeMonthlyChargesOfTheSameAmount_AreRecordedAsActive()
    {
        var found = RecurringDetector.Detect(
            [Debit(Today.AddDays(-61), 260_000, "Netflix"), Debit(Today.AddDays(-31), 260_000, "Netflix"), Debit(Today.AddDays(-1), 260_000, "Netflix")],
            None, Today, Rules).Single();

        found.Status.Should().Be(RecurringStatus.Active);
        found.Cadence.Should().Be(RecurringCadence.Monthly);
        found.AmountCents.Should().Be(260_000);
        found.Direction.Should().Be(RecurringDirection.Outflow);
        found.NextDueDate.Should().Be(Today.AddDays(-1).AddMonths(1));
    }

    [Fact]
    public void TwoOccurrences_AreOnlySuggested()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-30), 199_000, "Spotify"), Debit(Today, 199_000, "Spotify")], None, Today, Rules)
            .Single().Status.Should().Be(RecurringStatus.Suggested);

    [Fact]
    public void AmountsWithinTenPercent_StillCount()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-60), 600_000, "EVN"), Debit(Today.AddDays(-30), 640_000, "EVN"), Debit(Today, 580_000, "EVN")], None, Today, Rules)
            .Should().ContainSingle();

    [Fact]
    public void ErraticAmounts_AreNotASubscription()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-60), 50_000, "Circle K"), Debit(Today.AddDays(-30), 320_000, "Circle K"), Debit(Today, 90_000, "Circle K")],
                None, Today, Rules)
            .Should().BeEmpty();

    [Fact]
    public void BunchedTogether_IsNeitherWeeklyNorMonthly()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-4), 45_000, "Highlands"), Debit(Today.AddDays(-2), 45_000, "Highlands"), Debit(Today, 45_000, "Highlands")],
                None, Today, Rules)
            .Should().BeEmpty();

    [Fact]
    public void WeeklyCharges_AreDetectedAsWeekly()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-14), 120_000, "Gym"), Debit(Today.AddDays(-7), 120_000, "Gym"), Debit(Today, 120_000, "Gym")], None, Today, Rules)
            .Single().Cadence.Should().Be(RecurringCadence.Weekly);

    [Fact]
    public void MonthlySalary_IsAnInflow()
        => RecurringDetector.Detect(
                [Credit(Today.AddDays(-58), 15_000_000, "Cong ty ABC"), Credit(Today.AddDays(-28), 15_000_000, "Cong ty ABC"), Credit(Today.AddDays(2), 15_000_000, "Cong ty ABC")],
                None, Today.AddDays(2), Rules)
            .Single().Direction.Should().Be(RecurringDirection.Inflow);

    [Fact]
    public void AlreadyKnownOrRejectedCounterparty_IsNotSuggestedAgain()
    {
        var known = new HashSet<(RecurringDirection, string)> { (RecurringDirection.Outflow, "netflix") };

        RecurringDetector.Detect(
                [Debit(Today.AddDays(-30), 260_000, "Netflix"), Debit(Today, 260_000, "Netflix")], known, Today, Rules)
            .Should().BeEmpty();
    }

    [Fact]
    public void TransactionsMarkedOneOff_AreIgnored()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-30), 260_000, "Netflix", excluded: true), Debit(Today, 260_000, "Netflix", excluded: true)], None, Today, Rules)
            .Should().BeEmpty();

    [Fact]
    public void LongStoppedCharge_IsNotSuggested()
        => RecurringDetector.Detect(
                [Debit(Today.AddDays(-90), 260_000, "Netflix"), Debit(Today.AddDays(-60), 260_000, "Netflix")], None, Today, Rules)
            .Should().BeEmpty();
}
