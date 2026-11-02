using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class CashflowEngineTests
{
    private static SpendingBaseline Flat(long perDay)
    {
        var v = Enumerable.Repeat(perDay, 7).ToArray();
        return new SpendingBaseline(v, v.ToArray(), v.ToArray(), ForecastConfidence.Normal, 60, SpendingBaseline.BasisHistory);
    }

    private static CashflowInput Input(
        long balance, long perDay, IReadOnlyList<CashflowEvent>? events = null, long spentToday = 0, IReadOnlyList<ForecastHoliday>? holidays = null)
        => new(Today, Today.AddDays(9), balance, Flat(perDay), spentToday, events ?? [], holidays ?? []);

    [Fact]
    public void Balance_FallsByTheDailySpend()
    {
        var result = CashflowEngine.Project(Input(1_000_000, 100_000));

        result.Days.Should().HaveCount(10);
        result.Days[0].ExpectedBalanceCents.Should().Be(900_000);
        result.EndBalanceCents.Should().Be(0);
    }

    [Fact]
    public void SpendAlreadyMadeToday_IsNotSubtractedAgain()
        => CashflowEngine.Project(Input(1_000_000, 100_000, spentToday: 70_000)).Days[0].ExpectedBalanceCents.Should().Be(970_000);

    [Fact]
    public void KnownEvents_LandOnTheirDay_AndTheLowestDayIsReported()
    {
        var events = new[]
        {
            new CashflowEvent(Today.AddDays(3), -4_000_000, "Tiền nhà", CashflowEventKinds.RecurringOutflow, null),
            new CashflowEvent(Today.AddDays(5), 15_000_000, "Lương", CashflowEventKinds.RecurringInflow, null),
        };

        var result = CashflowEngine.Project(Input(5_000_000, 100_000, events));

        result.Days[3].ExpectedBalanceCents.Should().Be(5_000_000 - (4 * 100_000) - 4_000_000);
        result.LowestDay.Should().Be(Today.AddDays(4));
        result.MainCauses[0].Label.Should().Be("Tiền nhà");
    }

    [Fact]
    public void HolidayMultiplier_RaisesFlexibleSpending()
    {
        var tet = new ForecastHoliday { Name = "Tết", Start = Today.AddDays(1), End = Today.AddDays(1), MultiplierPercent = 150 };

        CashflowEngine.Project(Input(1_000_000, 100_000, holidays: [tet])).Days[1].FlexibleSpendCents.Should().Be(150_000);
    }

    [Fact]
    public void Scenario_IsJustAnotherInput_AndChangesNothingElse()
    {
        var baseInput = Input(1_000_000, 50_000);
        var before = CashflowEngine.Project(baseInput);
        var withShoes = CashflowEngine.Project(baseInput with
        {
            Events = [new CashflowEvent(Today.AddDays(2), -1_500_000, "Giày", CashflowEventKinds.Scenario, null)],
        });

        withShoes.EndBalanceCents.Should().Be(before.EndBalanceCents - 1_500_000);
        CashflowEngine.Project(baseInput).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void RecurringEvents_SkipAlreadyPaidDueDates()
    {
        var rent = Item("Tiền nhà", 4_000_000, Today.AddDays(1));

        var events = CashflowEngine.RecurringEvents([rent], [Debit(Today.AddDays(-1), 4_000_000, "Tiền nhà")], Today, Today.AddDays(40), Rules);

        events.Should().ContainSingle().Which.Date.Should().Be(Today.AddDays(1).AddMonths(1));
    }

    [Fact]
    public void RecurringEvents_OverdueWithinGrace_IsExpectedToday_AndBeyondGraceIsDropped()
    {
        var recent = Item("Điện", 600_000, Today.AddDays(-3));
        var stale = Item("Gym cũ", 300_000, Today.AddDays(-20));

        var events = CashflowEngine.RecurringEvents([recent, stale], [], Today, Today.AddDays(5), Rules);

        events.Should().ContainSingle().Which.Should().Match<CashflowEvent>(e => e.Label == "Điện" && e.Date == Today && e.AmountCents == -600_000);
    }

    [Fact]
    public void RecurringEvents_Income_IsPositive()
        => CashflowEngine.RecurringEvents(
                [Item("Lương", 15_000_000, Today.AddDays(3), RecurringDirection.Inflow)], [], Today, Today.AddDays(5), Rules)
            .Single().AmountCents.Should().Be(15_000_000);
}
