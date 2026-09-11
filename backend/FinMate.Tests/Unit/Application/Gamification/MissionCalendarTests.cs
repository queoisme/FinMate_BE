using FinMate.Application.Gamification;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Gamification;

public class MissionCalendarTests
{
    [Fact]
    public void Daily_PeriodIsJustThatDay()
    {
        var day = new DateOnly(2026, 9, 11);

        MissionCalendar.PeriodFor(MissionPeriodType.Daily, day).Should().Be((day, day));
    }

    [Theory]
    // 2026-09-07 là thứ Hai; mọi ngày trong tuần đó phải quy về cùng một chu kỳ.
    [InlineData("2026-09-07")]
    [InlineData("2026-09-11")]
    [InlineData("2026-09-13")]
    public void Weekly_AnyDayOfTheWeekMapsToTheSameMondayToSundayPeriod(string date)
    {
        var (start, end) = MissionCalendar.PeriodFor(MissionPeriodType.Weekly, DateOnly.Parse(date));

        start.Should().Be(new DateOnly(2026, 9, 7));
        end.Should().Be(new DateOnly(2026, 9, 13));
    }

    [Fact]
    public void Weekly_SundayBelongsToTheWeekThatStartedOnMonday_NotTheNextOne()
    {
        // Chủ nhật là DayOfWeek = 0 trong .NET, dễ bị tính thành đầu tuần mới.
        var (start, _) = MissionCalendar.PeriodFor(MissionPeriodType.Weekly, new DateOnly(2026, 9, 13));

        start.Should().Be(new DateOnly(2026, 9, 7));
    }

    [Fact]
    public void OneTime_AlwaysUsesTheSamePeriod_SoEachUserOnlyEverGetsOneRow()
    {
        var first = MissionCalendar.PeriodFor(MissionPeriodType.OneTime, new DateOnly(2026, 1, 1));
        var later = MissionCalendar.PeriodFor(MissionPeriodType.OneTime, new DateOnly(2030, 12, 31));

        first.Should().Be(later);
        first.Start.Should().Be(MissionCalendar.OneTimeEpoch);
    }
}
