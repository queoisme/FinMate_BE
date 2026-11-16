using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-05, D6, D8, D9, D17, S2: ai được nhắc, bao nhiêu lần mỗi đợt, cách nhau bao lâu, và lý do nào thắng.</summary>
public class ListenerHealthReminderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 2, 0, 0, TimeSpan.Zero);   // 09:00 giờ VN
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IListenerHealthReader> _reader = new();
    private readonly Mock<IForecastAlertRepository> _alerts = new();
    private readonly Mock<IForecastAlertDispatcher> _dispatcher = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly List<ForecastAlertRequest> _raised = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _history = [];
    private ListenerHealthSnapshot _snapshot = new([], [], null);

    public ListenerHealthReminderServiceTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
        _reader.Setup(r => r.ReadAsync(_userId, It.IsAny<DateTimeOffset>(), It.IsAny<ListenerHealthSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _snapshot);
        _alerts.Setup(a => a.ListCreatedWithKeyPrefixAsync(_userId, ForecastAlertKind.ListenerHealth, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ForecastAlertKind _, string prefix, CancellationToken _) => _history.TryGetValue(prefix, out var list) ? list.ToList() : []);
        _dispatcher.Setup(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ForecastAlertRequest, CancellationToken>((r, _) => _raised.Add(r))
            .ReturnsAsync(new ForecastAlert());
    }

    private ListenerHealthReminderService Service() => new(_reader.Object, _alerts.Object, _dispatcher.Object, _config.Object);

    private static FinancialAccount Account(string name, string package) => new() { Id = Guid.NewGuid(), AccountName = name, PackageName = package, IsMonitored = true };

    private static MonitoredAccountHealth Health(FinancialAccount account, AccountSilenceStatus status, int silentDays = 3, bool off = false, string? version = null)
        => new(account, new SilenceVerdict(status, Now.AddDays(-silentDays), 48, silentDays), off, version);

    private static DeviceHealth Device(DeviceHealthState state, params string[] packages)
        => new(
            new DeviceListenerStatus
            {
                Id = Guid.NewGuid(), InstallationId = Guid.NewGuid(), Manufacturer = "xiaomi",
                PermissionGranted = state != DeviceHealthState.PermissionLost,
                PermissionLostSince = state == DeviceHealthState.PermissionLost ? Now.AddHours(-5) : null,
            },
            state,
            packages.Select(p => new StoredPackage(p, true, null)).ToList());

    private void Given(params object[] items)
        => _snapshot = new ListenerHealthSnapshot(items.OfType<MonitoredAccountHealth>().ToList(), items.OfType<DeviceHealth>().ToList(), Now.AddDays(-60));

    [Fact]
    public async Task ASilentAccount_GetsOneAccountSilentReminder_WithNoAmount()
    {
        var vcb = Account("VCB", "com.VCB");
        Given(Health(vcb, AccountSilenceStatus.Silent), Device(DeviceHealthState.Ok, "com.VCB"));

        var result = await Service().RunForUserAsync(_userId, Now);

        result.RemindersRaised.Should().Be(1);
        var request = _raised.Single();
        request.Kind.Should().Be(ForecastAlertKind.ListenerHealth);
        request.HonorQuietHours.Should().BeTrue();
        request.DedupKey.Should().Be($"listener_health:account:{vcb.Id:N}:{Now.AddDays(-3).ToUnixTimeSeconds()}:1");
        request.Data["reason"].Should().Be("account_silent");
        request.Data["accountName"].Should().Be("VCB");
        request.Data["silentDays"].Should().Be("3");
        request.Data.Should().NotContainKey("hint");
        request.Data.Keys.Should().NotContain(k => k.Contains("amount", StringComparison.OrdinalIgnoreCase));
        request.Body.Should().Contain("3 ngày").And.Contain("VCB");
    }

    [Theory]
    [InlineData(AccountSilenceStatus.Ok)]
    [InlineData(AccountSilenceStatus.NotEnoughData)]
    public async Task OkOrSparseAccounts_AreNotReminded(AccountSilenceStatus status)
    {
        Given(Health(Account("MoMo", "com.mservice.momotransfer"), status, silentDays: 10));

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
        _raised.Should().BeEmpty();
    }

    [Fact]
    public async Task WithinTheCooldown_NoSecondReminder()
    {
        var vcb = Account("VCB", "com.VCB");
        Given(Health(vcb, AccountSilenceStatus.Silent));
        var prefix = $"listener_health:account:{vcb.Id:N}:{Now.AddDays(-3).ToUnixTimeSeconds()}:";
        _history[prefix] = [Now.AddDays(-1)];

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
    }

    [Fact]
    public async Task AfterTheCooldown_TheNextReminderIsNumberTwo()
    {
        var vcb = Account("VCB", "com.VCB");
        Given(Health(vcb, AccountSilenceStatus.Silent, silentDays: 6));
        var prefix = $"listener_health:account:{vcb.Id:N}:{Now.AddDays(-6).ToUnixTimeSeconds()}:";
        _history[prefix] = [Now.AddDays(-3).AddMinutes(5)];   // vài phút "sau" 09:00 ba ngày trước vẫn đủ ba ngày lịch

        await Service().RunForUserAsync(_userId, Now);

        _raised.Single().DedupKey.Should().Be(prefix + "2");
    }

    [Fact]
    public async Task AfterThreeReminders_TheEpisodeGoesQuiet()
    {
        var vcb = Account("VCB", "com.VCB");
        Given(Health(vcb, AccountSilenceStatus.Silent, silentDays: 20));
        var prefix = $"listener_health:account:{vcb.Id:N}:{Now.AddDays(-20).ToUnixTimeSeconds()}:";
        _history[prefix] = [Now.AddDays(-16), Now.AddDays(-12), Now.AddDays(-8)];

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
    }

    [Fact]
    public async Task ANewNotification_StartsANewEpisode()
    {
        var vcb = Account("VCB", "com.VCB");
        var oldPrefix = $"listener_health:account:{vcb.Id:N}:{Now.AddDays(-20).ToUnixTimeSeconds()}:";
        _history[oldPrefix] = [Now.AddDays(-16), Now.AddDays(-12), Now.AddDays(-8)];
        Given(Health(vcb, AccountSilenceStatus.Silent, silentDays: 3));    // mốc mới: thông báo cuối 3 ngày trước

        await Service().RunForUserAsync(_userId, Now);

        _raised.Single().DedupKey.Should().EndWith(":1");
    }

    [Fact]
    public async Task PermissionLost_GetsAPermissionLostReminder_KeyedByTheLossTime()
    {
        var device = Device(DeviceHealthState.PermissionLost, "com.VCB");
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Ok), device);

        await Service().RunForUserAsync(_userId, Now);

        var request = _raised.Single();
        request.DedupKey.Should().Be($"listener_health:device:{device.Status.Id:N}:{device.Status.PermissionLostSince!.Value.ToUnixTimeSeconds()}:1");
        request.Data["reason"].Should().Be("permission_lost");
        request.Data["manufacturer"].Should().Be("xiaomi");
    }

    [Fact]
    public async Task UnknownDevices_AreNotReminded()
    {
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Ok), Device(DeviceHealthState.Unknown, "com.VCB"));

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
    }

    [Fact]
    public async Task NoWatchedAccount_NoPermissionReminderEither()
    {
        Given(Device(DeviceHealthState.PermissionLost));

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
    }

    [Fact]
    public async Task PermissionLostAndSilent_OnlyThePermissionReminderIsSent()
    {
        var momo = Account("MoMo", "com.mservice.momotransfer");
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Silent), Health(momo, AccountSilenceStatus.Silent),
            Device(DeviceHealthState.PermissionLost, "com.VCB"));

        await Service().RunForUserAsync(_userId, Now);

        _raised.Select(r => r.Data["reason"]).Should().BeEquivalentTo("permission_lost", "account_silent");
        _raised.Single(r => r.Data["reason"] == "account_silent").Data["accountId"].Should().Be(momo.Id.ToString());
    }

    [Fact]
    public async Task ALostDeviceWithoutAPackageList_ExplainsEverySilence()
    {
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Silent), Device(DeviceHealthState.PermissionLost));

        await Service().RunForUserAsync(_userId, Now);

        _raised.Should().ContainSingle().Which.Data["reason"].Should().Be("permission_lost");
    }

    [Fact]
    public async Task BankNotificationsOff_AddsTheHint()
    {
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Silent, off: true));

        await Service().RunForUserAsync(_userId, Now);

        _raised.Single().Data["hint"].Should().Be("bank_notifications_off");
    }

    [Fact]
    public async Task UploadGap_GetsASyncPendingReminder_AndIsCountedByAppVersion()
    {
        var vcb = Account("VCB", "com.VCB");
        Given(Health(vcb, AccountSilenceStatus.UploadGap, silentDays: 4, version: "2.4.0"), Device(DeviceHealthState.Ok, "com.VCB"));

        var result = await Service().RunForUserAsync(_userId, Now);

        var request = _raised.Single();
        request.DedupKey.Should().StartWith($"listener_health:upload:{vcb.Id:N}:");
        request.Data["reason"].Should().Be("account_silent");
        request.Data["hint"].Should().Be("sync_pending");
        request.Body.Should().Contain("đồng bộ");
        result.UploadGapAppVersions.Should().Equal("2.4.0");
    }

    [Fact]
    public async Task ASwitchedOffUser_IsNotCountedAsReminded()
    {
        _dispatcher.Setup(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((ForecastAlert?)null);
        Given(Health(Account("VCB", "com.VCB"), AccountSilenceStatus.Silent));

        (await Service().RunForUserAsync(_userId, Now)).RemindersRaised.Should().Be(0);
    }

    [Fact]
    public async Task ALongAccountName_IsShortenedInTheTitleOnly()
    {
        var name = new string('A', 80);
        Given(Health(Account(name, "com.VCB"), AccountSilenceStatus.Silent));

        await Service().RunForUserAsync(_userId, Now);

        _raised.Single().Title.Length.Should().BeLessThanOrEqualTo(120);
        _raised.Single().Data["accountName"].Should().Be(name);
    }
}
