using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class ForecastAlertDispatcherTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _user;
    private readonly Mock<IForecastAlertRepository> _alerts = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPushNotificationService> _push = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private DateTimeOffset _now = new(Today.ToDateTime(new TimeOnly(10, 0)), VietnamTime.Offset);

    public ForecastAlertDispatcherTests()
    {
        _user = new User { Id = _userId };
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        _alerts.Setup(r => r.TryAddAsync(It.IsAny<ForecastAlert>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private ForecastAlertDispatcher Dispatcher() => new(_alerts.Object, _users.Object, _push.Object, _config.Object, () => _now);

    private ForecastAlertRequest Request(ForecastAlertKind kind = ForecastAlertKind.BillDue, bool quiet = false)
        => new(_userId, kind, "key-1", "Tiêu đề", "Nội dung", new Dictionary<string, string> { ["dueDate"] = "2026-11-05" }, quiet);

    [Fact]
    public async Task NewAlert_IsStoredAsSent_AndPushedWithItsIdInTheData()
    {
        var alert = await Dispatcher().RaiseAsync(Request());

        alert!.Status.Should().Be(ForecastAlertStatus.Sent);
        _push.Verify(p => p.NotifyAsync(_userId, "Tiêu đề", "Nội dung",
            It.Is<IReadOnlyDictionary<string, string>>(d => d["alertId"] == alert.Id.ToString() && d["kind"] == "bill_due" && d["dueDate"] == "2026-11-05"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SameDedupKey_IsRaisedOnlyOnce()
    {
        _alerts.Setup(r => r.ExistsAsync(_userId, ForecastAlertKind.BillDue, "key-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await Dispatcher().RaiseAsync(Request())).Should().BeNull();
        _push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForecastAlertsSwitchedOff_NothingIsRaised()
    {
        _user.NotificationPrefs.ForecastAlertsEnabled = false;

        (await Dispatcher().RaiseAsync(Request())).Should().BeNull();
        _alerts.Verify(r => r.TryAddAsync(It.IsAny<ForecastAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BudgetRisk_FollowsTheBudgetAlertSwitch_NotTheForecastOne()
    {
        _user.NotificationPrefs.ForecastAlertsEnabled = false;

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.BudgetRisk))).Should().NotBeNull();
    }

    [Fact]
    public async Task ListenerHealth_FollowsItsOwnSwitch_NotTheForecastOne()
    {
        _user.NotificationPrefs.ForecastAlertsEnabled = false;

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.ListenerHealth))).Should().NotBeNull();
    }

    [Fact]
    public async Task ListenerHealthSwitchedOff_NothingIsRaised()
    {
        _user.NotificationPrefs.ListenerHealthAlertsEnabled = false;

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.ListenerHealth))).Should().BeNull();
    }

    [Fact]
    public async Task LowBalance_WithinCooldown_IsSkipped()
    {
        _alerts.Setup(r => r.AnyCreatedSinceAsync(_userId, ForecastAlertKind.LowBalance, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.LowBalance, quiet: true))).Should().BeNull();
    }

    [Fact]
    public async Task LowBalance_RecentlyMarkedInaccurate_IsSkipped()
    {
        _alerts.Setup(r => r.AnyMarkedInaccurateSinceAsync(_userId, ForecastAlertKind.LowBalance, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.LowBalance, quiet: true))).Should().BeNull();
    }

    [Fact]
    public async Task LowBalance_DuringQuietHours_WaitsUntilMorning_WithoutPushing()
    {
        _now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(23, 30)), VietnamTime.Offset);

        var alert = await Dispatcher().RaiseAsync(Request(ForecastAlertKind.LowBalance, quiet: true));

        alert!.Status.Should().Be(ForecastAlertStatus.Pending);
        alert.ScheduledFor.Should().Be(new DateTimeOffset(Today.AddDays(1).ToDateTime(new TimeOnly(7, 0)), VietnamTime.Offset));
        _push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UserQuietHours_OverrideTheSystemDefault()
    {
        _now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(23, 30)), VietnamTime.Offset);
        _user.QuietHoursStartMinute = 1;
        _user.QuietHoursEndMinute = 2;

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.LowBalance, quiet: true)))!.Status.Should().Be(ForecastAlertStatus.Sent);
    }

    [Fact]
    public async Task OtherKinds_IgnoreQuietHours()
    {
        _now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(23, 30)), VietnamTime.Offset);

        (await Dispatcher().RaiseAsync(Request(ForecastAlertKind.BillDue)))!.Status.Should().Be(ForecastAlertStatus.Sent);
    }

    [Fact]
    public async Task DispatchDue_SendsPendingAlerts()
    {
        var pending = new ForecastAlert
        {
            Id = Guid.NewGuid(), UserId = _userId, Kind = ForecastAlertKind.LowBalance, Title = "T", Body = "B",
            Data = "{\"action\":\"forecast_alert\"}", Status = ForecastAlertStatus.Pending, ScheduledFor = _now.AddMinutes(-1),
        };
        _alerts.Setup(r => r.ListDuePendingAsync(_now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([pending]);

        (await Dispatcher().DispatchDueAsync()).Should().Be(1);

        pending.Status.Should().Be(ForecastAlertStatus.Sent);
        _push.Verify(p => p.NotifyAsync(_userId, "T", "B", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NextMorning_IsTodayBeforeEight_AndTomorrowAfter()
    {
        var early = new DateTimeOffset(Today.ToDateTime(new TimeOnly(1, 30)), VietnamTime.Offset);
        var late = new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), VietnamTime.Offset);

        ForecastAlertKinds.NextMorning(early).Should().Be(new DateTimeOffset(Today.ToDateTime(new TimeOnly(8, 0)), VietnamTime.Offset));
        ForecastAlertKinds.NextMorning(late).Should().Be(new DateTimeOffset(Today.AddDays(1).ToDateTime(new TimeOnly(8, 0)), VietnamTime.Offset));
    }
}
