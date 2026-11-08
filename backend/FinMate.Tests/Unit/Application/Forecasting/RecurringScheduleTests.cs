using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class RecurringScheduleTests
{
    [Fact]
    public void Monthly_OnThe31st_ClampsInShortMonthsAndComesBack()
    {
        var feb = RecurringSchedule.Next(new DateOnly(2027, 1, 31), RecurringCadence.Monthly, 31);
        var mar = RecurringSchedule.Next(feb, RecurringCadence.Monthly, 31);

        feb.Should().Be(new DateOnly(2027, 2, 28));
        mar.Should().Be(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public void Weekly_AddsSevenDays()
        => RecurringSchedule.Next(new DateOnly(2026, 11, 2), RecurringCadence.Weekly).Should().Be(new DateOnly(2026, 11, 9));

    [Fact]
    public void Yearly_OnLeapDay_FallsBackToThe28th()
        => RecurringSchedule.Next(new DateOnly(2028, 2, 29), RecurringCadence.Yearly, 29).Should().Be(new DateOnly(2029, 2, 28));

    [Fact]
    public void Occurrences_StartFromTheFirstDueDateInsideTheRange()
    {
        var item = ForecastTestData.Item("Tiền nhà", 4_000_000, new DateOnly(2026, 10, 10));

        RecurringSchedule.Occurrences(item, new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31))
            .Should().Equal(new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 10), new DateOnly(2027, 1, 10));
    }
}
