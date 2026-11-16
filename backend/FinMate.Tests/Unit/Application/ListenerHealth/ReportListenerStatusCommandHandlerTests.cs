using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.ListenerHealth;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-01, D1, D2, D5, S6: kiểm đầu vào, giới hạn thiết bị, lọc package lạ, kẹp giờ tương lai, xoá cache độ phủ.</summary>
public class ReportListenerStatusCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 2, 0, 0, TimeSpan.Zero);
    private const string Vcb = "com.VCB";
    private const string Momo = "com.mservice.momotransfer";

    private readonly Mock<IListenerHealthRepository> _repository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private ListenerReport? _recorded;

    public ReportListenerStatusCommandHandlerTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
        _repository.Setup(r => r.GetKnownPackagesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { Vcb, Momo });
        _repository.Setup(r => r.RecordReportAsync(It.IsAny<ListenerReport>(), It.IsAny<CancellationToken>()))
            .Callback<ListenerReport, CancellationToken>((r, _) => _recorded = r)
            .Returns(Task.CompletedTask);
    }

    private ReportListenerStatusCommandHandler Handler()
        => new(_repository.Object, _config.Object, _cache.Object, new ReportListenerStatusCommandValidator(), () => Now);

    private static ReportListenerStatusCommand Command(Guid user, IReadOnlyList<ListenerPackageReport>? packages = null, bool? permission = true, Guid? installation = null)
        => new(user, installation ?? Guid.NewGuid(), permission, true, packages, " 2.4.0 ", "Xiaomi");

    private List<StoredPackage> StoredPackages()
        => JsonSerializer.Deserialize<List<StoredPackage>>(_recorded!.SelectedPackagesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public async Task UnknownPackages_AreDroppedSilently()
    {
        await Handler().HandleAsync(Command(Guid.NewGuid(), [new(Vcb, true, Now.AddHours(-1)), new("com.facebook.katana", true, Now)]));

        StoredPackages().Select(p => p.PackageName).Should().Equal(Vcb);
        _recorded!.SelectedPackagesJson.Should().NotContain("facebook");
    }

    [Fact]
    public async Task DuplicatePackages_KeepTheLatestLastPostedAt()
    {
        await Handler().HandleAsync(Command(Guid.NewGuid(), [new(Vcb, null, Now.AddHours(-5)), new(Vcb, false, Now.AddHours(-1))]));

        var stored = StoredPackages().Should().ContainSingle().Subject;
        stored.LastPostedAt.Should().Be(Now.AddHours(-1));
        stored.NotificationsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(5, 5)]       // trong độ lệch 10 phút: giữ nguyên
    [InlineData(60 * 24, 0)] // ngày mai: kẹp về giờ server
    public async Task LastPostedAtInTheFuture_IsClamped(int minutesAhead, int expectedMinutesAhead)
    {
        await Handler().HandleAsync(Command(Guid.NewGuid(), [new(Momo, true, Now.AddMinutes(minutesAhead))]));

        StoredPackages().Single().LastPostedAt.Should().Be(Now.AddMinutes(expectedMinutesAhead));
    }

    [Fact]
    public async Task VersionAndManufacturer_AreTrimmed_AndTheManufacturerLowercased()
    {
        await Handler().HandleAsync(Command(Guid.NewGuid()));

        _recorded!.AppVersion.Should().Be("2.4.0");
        _recorded.Manufacturer.Should().Be("xiaomi");
        _recorded.Day.Should().Be(new DateOnly(2026, 11, 14));
        _recorded.SelectedPackagesJson.Should().Be("[]");
    }

    [Fact]
    public async Task AReport_ClearsTheForecastCoverageCache()
    {
        var user = Guid.NewGuid();

        await Handler().HandleAsync(Command(user));

        _cache.Verify(c => c.RemoveAsync(DataCoverageCache.Key(user, 90), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ANewDeviceBeyondTheLimit_Is422()
    {
        _repository.Setup(r => r.CountDevicesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(10);

        var thrown = await FluentActions.Awaiting(() => Handler().HandleAsync(Command(Guid.NewGuid()))).Should().ThrowAsync<BusinessRuleException>();

        thrown.Which.ErrorCode.Should().Be(ListenerHealthErrorCodes.TooManyDevices);
        _repository.Verify(r => r.RecordReportAsync(It.IsAny<ListenerReport>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AKnownDevice_StillReports_WhenTheUserIsAtTheLimit()
    {
        _repository.Setup(r => r.CountDevicesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(10);
        _repository.Setup(r => r.DeviceExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Handler().HandleAsync(Command(Guid.NewGuid()));

        _recorded.Should().NotBeNull();
    }

    public static TheoryData<ReportListenerStatusCommand> Invalid() => new()
    {
        new(Guid.NewGuid(), null, true, true, null, null, null),
        new(Guid.NewGuid(), Guid.Empty, true, true, null, null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), null, true, null, null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, null, null, null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, true, Enumerable.Range(0, 51).Select(i => new ListenerPackageReport($"com.p{i}", true, null)).ToList(), null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, true, [new ListenerPackageReport("", true, null)], null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, true, [new ListenerPackageReport(new string('a', 256), true, null)], null, null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, true, null, new string('1', 33), null),
        new(Guid.NewGuid(), Guid.NewGuid(), true, true, null, null, "Sam\nsung"),
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task InvalidReports_AreRejected(ReportListenerStatusCommand command)
    {
        await FluentActions.Awaiting(() => Handler().HandleAsync(command)).Should().ThrowAsync<ValidationException>();
    }
}
