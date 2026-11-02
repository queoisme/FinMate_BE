using FinMate.Application.FinanceMethods;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class SpendableCalculatorTests
{
    private static SpendableInput Input(long balance, IReadOnlyList<RecurringItemView>? items = null, long goals = 0, long reserve = 0)
        => new(Today, [new SpendableAccount(Account, "Ví", balance)], items ?? [], [], goals, Array.Empty<SpendableGoalDto>(), reserve, Rules);

    [Fact]
    public void WithoutKnownIncome_ThePeriodRunsToMonthEnd()
    {
        var result = SpendableCalculator.Compute(Input(2_900_000));

        result.PeriodEnd.Should().Be(new DateOnly(2026, 11, 30));
        result.PeriodBasis.Should().Be(SpendablePeriodBasis.MonthEnd);
        result.DaysRemaining.Should().Be(29);
        result.PerDayCents.Should().Be(100_000);
    }

    [Fact]
    public void WithAPayday_ThePeriodEndsTheDayBefore()
    {
        var salary = Item("Lương", 15_000_000, Today.AddDays(3), RecurringDirection.Inflow);

        var result = SpendableCalculator.Compute(Input(1_000_000, [salary]));

        result.PeriodEnd.Should().Be(Today.AddDays(2));
        result.PeriodBasis.Should().Be(SpendablePeriodBasis.NextIncome);
        result.ExpectedIncomeCents.Should().Be(0, "lương rơi vào ngày đầu kỳ sau, không thuộc kỳ này");
    }

    [Fact]
    public void BillsGoalsAndReserve_AreTakenOut()
    {
        var rent = Item("Tiền nhà", 4_000_000, Today.AddDays(8));

        var result = SpendableCalculator.Compute(Input(8_000_000, [rent], goals: 1_000_000, reserve: 750_000));

        result.UpcomingBillsCents.Should().Be(4_000_000);
        result.SpendableCents.Should().Be(8_000_000 - 4_000_000 - 1_000_000 - 750_000);
        result.Bills.Should().ContainSingle(b => b.Label == "Tiền nhà" && b.Date == Today.AddDays(8));
    }

    [Fact]
    public void Shortfall_ShowsZeroAndHowMuchIsMissing()
    {
        var rent = Item("Tiền nhà", 4_000_000, Today.AddDays(8));

        var result = SpendableCalculator.Compute(Input(3_000_000, [rent]));

        result.SpendableCents.Should().Be(0);
        result.PerDayCents.Should().Be(0);
        result.ShortfallCents.Should().Be(1_000_000);
    }
}
