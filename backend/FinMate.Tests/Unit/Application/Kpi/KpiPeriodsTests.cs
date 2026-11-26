using FinMate.Application.Common.Exceptions;
using FinMate.Application.Kpi;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Kpi;

public class KpiPeriodsTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    [Fact]
    public void Validate_DefaultsToDay_AndAcceptsExactly366DaysInclusive()
    {
        var (from, to, g) = KpiPeriods.Validate(new DateOnly(2025, 11, 27), Today, null, Today);

        g.Should().Be(KpiGranularity.Day);
        (to.DayNumber - from.DayNumber + 1).Should().Be(366);
    }

    [Theory]
    [InlineData("2025-11-26", "2026-11-27", "day")] // 367 ngày
    [InlineData("2026-11-20", "2026-11-10", "day")] // from sau to
    [InlineData("2026-11-20", "2026-11-28", "day")] // to sau hôm nay
    [InlineData("2026-11-20", "2026-11-27", "year")]
    [InlineData("2026-11-20", "2026-11-27", "1")]
    public void Validate_RefusesBadInput_With422InvalidRange(string from, string to, string granularity)
    {
        var act = () => KpiPeriods.Validate(DateOnly.Parse(from), DateOnly.Parse(to), granularity, Today);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(KpiErrorCodes.InvalidRange);
    }

    [Fact]
    public void Validate_RefusesMissingDates()
    {
        var act = () => KpiPeriods.Validate(null, Today, "day", Today);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(KpiErrorCodes.InvalidRange);
    }

    [Fact]
    public void Split_Week_StartsOnMonday_CutsTheEnds_AndMarksPartial()
    {
        // 2026-11-04 là thứ Tư, 2026-11-22 là Chủ nhật.
        var periods = KpiPeriods.Split(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 22), KpiGranularity.Week, Today);

        periods.Should().Equal(
            new KpiPeriod(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 8), true),
            new KpiPeriod(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15), false),
            new KpiPeriod(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 22), false));
    }

    [Fact]
    public void Split_ThePeriodHoldingTodayIsPartial_EvenWhenNotCut()
    {
        var periods = KpiPeriods.Split(new DateOnly(2026, 11, 23), Today, KpiGranularity.Week, Today);

        periods.Should().ContainSingle().Which.Should().Be(new KpiPeriod(new DateOnly(2026, 11, 23), Today, true));
        KpiPeriods.Split(Today, Today, KpiGranularity.Day, Today).Single().Partial.Should().BeTrue();
        KpiPeriods.Split(Today.AddDays(-1), Today.AddDays(-1), KpiGranularity.Day, Today).Single().Partial.Should().BeFalse();
    }

    [Fact]
    public void Split_Month_FollowsTheCalendar()
    {
        var periods = KpiPeriods.Split(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 31), KpiGranularity.Month, Today);

        periods.Select(p => (p.Start, p.End, p.Partial)).Should().Equal(
            (new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31), true),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), false),
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), false));
    }

    [Fact]
    public void PeriodBounds_AreMidnightVietnamTime_AtOffsetZero()
    {
        var period = new KpiPeriod(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), false);

        period.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 17, 0, 0, TimeSpan.Zero));
        period.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.Zero));
        period.StartUtc.Offset.Should().Be(TimeSpan.Zero);
    }
}
