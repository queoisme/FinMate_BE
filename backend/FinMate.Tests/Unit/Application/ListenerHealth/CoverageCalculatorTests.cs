using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-07, S3, S4, D7: % số ngày có ít nhất một thiết bị ok; ngày không báo kế thừa ≤ 36 giờ; ngày trước ví đầu tiên / lần báo đầu tiên không tính.</summary>
public class CoverageCalculatorTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static readonly DateOnly Today = new(2026, 11, 14);
    private static readonly CoverageThresholds Thresholds = new(GoodPercent: 85, MinPercent: 60, InheritHours: 36);
    private static readonly DateTimeOffset LongAgo = new(2026, 1, 1, 0, 0, 0, Vn);

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), Vn);

    private static DeviceListenerDay Day(Guid device, DateOnly day, bool ok = true, int hour = 10, bool? lastOk = null)
        => new()
        {
            StatusId = device,
            Day = day,
            AnyPermissionDenied = !ok,
            LastPermissionGranted = lastOk ?? ok,
            LastListenerConnected = lastOk ?? ok,
            LastReportedAt = At(day, hour),
            ReportCount = 1,
        };

    private static CoverageDevice Device(Guid id, IEnumerable<DeviceListenerDay> days)
    {
        var list = days.OrderBy(d => d.Day).ToList();
        return new CoverageDevice(id, list.Count == 0 ? LongAgo : list[0].LastReportedAt, list);
    }

    private static IEnumerable<DateOnly> LastDays(int count) => Enumerable.Range(0, count).Select(i => Today.AddDays(-i));

    private static CoverageResult Compute(int windowDays, DateTimeOffset? firstMonitoredAt, params CoverageDevice[] devices)
        => CoverageCalculator.Compute(Today, windowDays, firstMonitoredAt, devices, Thresholds);

    [Fact]
    public void OneDevice_OkEveryDay_IsHundredPercent_Good()
    {
        var device = Guid.NewGuid();

        var result = Compute(30, LongAgo, Device(device, LastDays(30).Select(d => Day(device, d))));

        result.Percent.Should().Be(100);
        result.Level.Should().Be(CoverageLevel.Good);
        result.WindowDays.Should().Be(30);
        result.CountedDays.Should().Be(30);
    }

    [Fact]
    public void HalfTheDaysNotOk_IsFiftyPercent_Poor()
    {
        var device = Guid.NewGuid();
        var days = LastDays(30).Select((d, i) => Day(device, d, ok: i % 2 == 0));

        var result = Compute(30, LongAgo, Device(device, days));

        result.Percent.Should().Be(50);
        result.Level.Should().Be(CoverageLevel.Poor);
    }

    [Fact]
    public void TwoDevices_CoverForEachOther()
    {
        var phone = Guid.NewGuid();
        var tablet = Guid.NewGuid();
        var phoneDays = LastDays(30).Select((d, i) => Day(phone, d, ok: i % 2 == 0));
        var tabletDays = LastDays(30).Select((d, i) => Day(tablet, d, ok: i % 2 == 1));

        Compute(30, LongAgo, Device(phone, phoneDays), Device(tablet, tabletDays)).Percent.Should().Be(100);
    }

    [Fact]
    public void ADayWithAnyPermissionDenial_IsNotOk_EvenIfTheLastReportWasFine()
    {
        var device = Guid.NewGuid();
        var days = LastDays(10).Select(d => d == Today.AddDays(-3) ? Day(device, d, ok: false, lastOk: true) : Day(device, d));

        Compute(10, LongAgo, Device(device, days)).Percent.Should().Be(90);
    }

    [Fact]
    public void ADisconnectThatRecoversWithinTheDay_StaysOk()
    {
        var device = Guid.NewGuid();
        var day = Day(device, Today);
        day.AnyPermissionDenied = false;
        day.LastListenerConnected = true;   // trước đó trong ngày từng báo connected=false

        DeviceListenerDay[] rows = [day];
        rows[0].PermissionOk.Should().BeTrue();
        Compute(1, LongAgo, Device(device, rows)).Percent.Should().Be(100);
    }

    [Fact]
    public void LastReportOfTheDayDisconnected_MakesTheDayNotOk()
    {
        var device = Guid.NewGuid();
        var day = Day(device, Today);
        day.LastListenerConnected = false;

        Compute(1, LongAgo, Device(device, [day])).Percent.Should().Be(0);
    }

    [Fact]
    public void OneMissedDay_InheritsTheReportBeforeIt()
    {
        var device = Guid.NewGuid();
        var days = LastDays(10).Where(d => d != Today.AddDays(-4)).Select(d => Day(device, d, hour: 8));

        Compute(10, LongAgo, Device(device, days)).Percent.Should().Be(100);
    }

    [Fact]
    public void TwoMissedDays_OnlyTheFirstIsInherited()
    {
        var device = Guid.NewGuid();
        var missing = new[] { Today.AddDays(-4), Today.AddDays(-5) };
        var days = LastDays(10).Where(d => !missing.Contains(d)).Select(d => Day(device, d, hour: 23));

        Compute(10, LongAgo, Device(device, days)).Percent.Should().Be(90);
    }

    [Fact]
    public void AMissedDayAfterANotOkReport_InheritsNotOk()
    {
        var device = Guid.NewGuid();
        var days = LastDays(10).Where(d => d != Today.AddDays(-4))
            .Select(d => d == Today.AddDays(-5) ? Day(device, d, ok: false) : Day(device, d));

        Compute(10, LongAgo, Device(device, days)).Percent.Should().Be(80);
    }

    [Fact]
    public void DaysBeforeTheFirstMonitoredAccount_AreNotCounted()
    {
        var device = Guid.NewGuid();

        var result = Compute(30, At(Today.AddDays(-9), 15), Device(device, LastDays(30).Select(d => Day(device, d))));

        result.CountedDays.Should().Be(10);
        result.Percent.Should().Be(100);
    }

    [Fact]
    public void DaysBeforeTheFirstReport_AreNotCounted()
    {
        var device = Guid.NewGuid();

        var result = Compute(30, LongAgo, Device(device, LastDays(5).Select(d => Day(device, d))));

        result.CountedDays.Should().Be(5);
        result.Percent.Should().Be(100);
    }

    [Fact]
    public void NoDeviceEverReported_IsUnknown_NotPoor()
    {
        var result = Compute(30, LongAgo);

        result.Percent.Should().BeNull();
        result.Level.Should().Be(CoverageLevel.Unknown);
    }

    [Fact]
    public void NoMonitoredAccount_IsUnknown()
    {
        var device = Guid.NewGuid();

        Compute(30, null, Device(device, LastDays(30).Select(d => Day(device, d)))).Level.Should().Be(CoverageLevel.Unknown);
    }

    [Theory]
    [InlineData(17, 20, CoverageLevel.Good)]      // 85%
    [InlineData(16, 20, CoverageLevel.Partial)]   // 80%
    [InlineData(12, 20, CoverageLevel.Partial)]   // 60%
    [InlineData(11, 20, CoverageLevel.Poor)]      // 55%
    public void Levels_FollowTheThresholds(int okDays, int window, CoverageLevel expected)
    {
        var device = Guid.NewGuid();
        var days = LastDays(window).Select((d, i) => Day(device, d, ok: i < okDays));

        Compute(window, LongAgo, Device(device, days)).Level.Should().Be(expected);
    }

    [Fact]
    public void Percent_IsRoundedDown_SoAlmostGoodStaysPartial()
    {
        var device = Guid.NewGuid();
        var days = LastDays(33).Select((d, i) => Day(device, d, ok: i < 28));    // 84,8%

        var result = Compute(33, LongAgo, Device(device, days));

        result.Percent.Should().Be(84);
        result.Level.Should().Be(CoverageLevel.Partial);
    }
}
