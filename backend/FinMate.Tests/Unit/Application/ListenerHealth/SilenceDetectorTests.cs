using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.ListenerHealth;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-03, D5/S2, D19: khoảng im lặng cho phép = max(48h, 3 × P90 khoảng cách), ví ít dùng không bị xét, listener còn thấy app thì là <c>upload_gap</c>.</summary>
public class SilenceDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 2, 0, 0, TimeSpan.Zero);
    private static readonly ListenerHealthSettings Settings = new();

    /// <summary>Hai thông báo mỗi ngày (08:00 và 20:00 giờ VN) trong <paramref name="days"/> ngày, kết thúc trước hiện tại <paramref name="silentHours"/> giờ.</summary>
    private static List<DateTimeOffset> TwicePerDay(int days, double silentHours)
    {
        var last = Now.AddHours(-silentHours);
        return Enumerable.Range(0, days * 2).Select(i => last.AddHours(-12 * i)).Reverse().ToList();
    }

    [Fact]
    public void RegularHistory_ThenThreeDaysQuiet_IsSilent()
    {
        var verdict = SilenceDetector.Evaluate(TwicePerDay(30, 72), null, Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.Silent);
        verdict.AllowedHours.Should().Be(48);           // P90 = 12h → 36h < sàn 48h
        verdict.SilentDays.Should().Be(3);
        verdict.LastNotificationAt.Should().Be(Now.AddHours(-72));
    }

    [Fact]
    public void RegularHistory_QuietForOneDay_IsOk()
    {
        SilenceDetector.Evaluate(TwicePerDay(30, 24), null, Now, Settings).Status.Should().Be(AccountSilenceStatus.Ok);
    }

    [Fact]
    public void SilenceExactlyAtTheAllowance_IsStillOk()
    {
        SilenceDetector.Evaluate(TwicePerDay(30, 48), null, Now, Settings).Status.Should().Be(AccountSilenceStatus.Ok);
    }

    [Fact]
    public void SparseHistory_IsNotEnoughData_EvenAfterTenQuietDays()
    {
        var history = new List<DateTimeOffset> { Now.AddDays(-28), Now.AddDays(-19), Now.AddDays(-10) };

        var verdict = SilenceDetector.Evaluate(history, null, Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.NotEnoughData);
        verdict.LastNotificationAt.Should().Be(Now.AddDays(-10));
        verdict.AllowedHours.Should().BeNull();
    }

    [Fact]
    public void EmptyHistory_IsNotEnoughData()
    {
        var verdict = SilenceDetector.Evaluate([], null, Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.NotEnoughData);
        verdict.LastNotificationAt.Should().BeNull();
    }

    [Theory]
    [InlineData(7, AccountSilenceStatus.NotEnoughData)]
    [InlineData(8, AccountSilenceStatus.Silent)]
    public void TheBaselineNeedsEightNotifications(int count, AccountSilenceStatus expected)
    {
        var last = Now.AddDays(-5);
        var history = Enumerable.Range(0, count).Select(i => last.AddHours(-6 * i)).ToList();

        SilenceDetector.Evaluate(history, null, Now, Settings).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData(60, AccountSilenceStatus.Ok)]
    [InlineData(80, AccountSilenceStatus.Silent)]
    public void DailyHistory_AllowsThreeTimesTheNinetiethPercentile(double silentHours, AccountSilenceStatus expected)
    {
        var last = Now.AddHours(-silentHours);
        var history = Enumerable.Range(0, 25).Select(i => last.AddDays(-i)).ToList();

        var verdict = SilenceDetector.Evaluate(history, null, Now, Settings);

        verdict.AllowedHours.Should().Be(72);
        verdict.Status.Should().Be(expected);
    }

    [Fact]
    public void NinetiethPercentile_IsNearestRank_SoOneLongGapDoesNotDominate()
    {
        // 19 khoảng 6h và 1 khoảng 100h: nearest-rank P90 của 20 khoảng là phần tử thứ 18 → 6h.
        var times = new List<DateTimeOffset> { Now.AddDays(-20) };
        times.Add(times[^1].AddHours(100));
        for (var i = 0; i < 19; i++)
        {
            times.Add(times[^1].AddHours(6));
        }

        SilenceDetector.Evaluate(times, null, Now, Settings).AllowedHours.Should().Be(48);
    }

    [Fact]
    public void ListenerStillSeesTheApp_IsAnUploadGap_NotSilent()
    {
        var verdict = SilenceDetector.Evaluate(TwicePerDay(30, 96), Now.AddHours(-5), Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.UploadGap);
        verdict.SilentDays.Should().Be(4);
        verdict.LastNotificationAt.Should().Be(Now.AddHours(-96));
    }

    [Fact]
    public void ListenerSawTheAppLongAgo_IsStillSilent()
    {
        SilenceDetector.Evaluate(TwicePerDay(30, 96), Now.AddHours(-90), Now, Settings).Status.Should().Be(AccountSilenceStatus.Silent);
    }

    [Fact]
    public void LastPostedAtInTheFuture_IsClampedToNow()
    {
        SilenceDetector.Evaluate(TwicePerDay(30, 96), Now.AddDays(3), Now, Settings).Status.Should().Be(AccountSilenceStatus.UploadGap);
    }

    [Fact]
    public void ReceivedAtInTheFuture_IsClampedToNow_AndDoesNotHideASilence()
    {
        var history = TwicePerDay(30, 72);
        history.Add(Now.AddDays(2));

        var verdict = SilenceDetector.Evaluate(history, null, Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.Ok);
        verdict.LastNotificationAt.Should().Be(Now);
    }

    [Fact]
    public void LongSilence_StaysSilent_BecauseTheBaselineEndsAtTheLastNotification()
    {
        var verdict = SilenceDetector.Evaluate(TwicePerDay(30, 40 * 24), null, Now, Settings);

        verdict.Status.Should().Be(AccountSilenceStatus.Silent);
        verdict.SilentDays.Should().Be(40);
    }

    [Fact]
    public void NotificationsOlderThanTheWindow_DoNotCountTowardsTheBaseline()
    {
        // 5 thông báo gần đây cộng 10 thông báo cũ hơn 30 ngày trước thông báo cuối.
        var last = Now.AddDays(-4);
        var history = Enumerable.Range(0, 5).Select(i => last.AddHours(-12 * i))
            .Concat(Enumerable.Range(0, 10).Select(i => last.AddDays(-31 - i)))
            .ToList();

        SilenceDetector.Evaluate(history, null, Now, Settings).Status.Should().Be(AccountSilenceStatus.NotEnoughData);
    }

    [Fact]
    public void IdenticalTimestamps_CountAsZeroGaps()
    {
        var last = Now.AddDays(-3);
        var history = Enumerable.Repeat(last, 10).ToList();

        var verdict = SilenceDetector.Evaluate(history, null, Now, Settings);

        verdict.AllowedHours.Should().Be(48);
        verdict.Status.Should().Be(AccountSilenceStatus.Silent);
    }
}
