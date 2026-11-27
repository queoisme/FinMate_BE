using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Ops;

public class OpsNotificationServiceTests
{
    private readonly Mock<IOpsNotificationRepository> _repository = new();
    private readonly Mock<IOpsAccessService> _access = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly List<OpsNotification> _added = [];
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();

    public OpsNotificationServiceTests()
    {
        UseSettings(new OpsNotificationSettings());
        _repository.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyList<OpsNotification>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<OpsNotification>, CancellationToken>((rows, _) => _added.AddRange(rows))
            .Returns(Task.CompletedTask);

        // Staff không có quyền điều phối ticket; OpsManager và Admin có.
        _access.Setup(a => a.HasAsync(It.IsAny<UserRole>(), OpsCapability.ManageTickets, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserRole role, OpsCapability _, CancellationToken _) => role != UserRole.Staff);
        _repository.Setup(r => r.ListActiveUserIdsByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<UserRole> roles, CancellationToken _) =>
                roles.Contains(UserRole.Staff) ? [_manager, _admin, Guid.NewGuid()] : [_manager, _admin]);
        _repository.Setup(r => r.ListUsersWithUnreadAsync(It.IsAny<OpsNotificationType>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
    }

    private OpsNotificationService Service()
        => new(_repository.Object, _access.Object, _config.Object, NullLogger<OpsNotificationService>.Instance);

    private void UseSettings(OpsNotificationSettings settings)
        => _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument { OpsNotifications = settings }, null));

    private Task NotifyTicketAsync(IReadOnlyCollection<Guid>? exclude = null)
        => Service().NotifyCapabilityAsync(OpsNotificationType.TicketNew, OpsCapability.ManageTickets, "Tiêu đề", "Nội dung", "SupportTicket", Guid.NewGuid(), exclude);

    [Theory]
    [InlineData(OpsNotificationType.TicketNew, "ticket_new")]
    [InlineData(OpsNotificationType.CommunityImagePending, "community_image_pending")]
    [InlineData(OpsNotificationType.DataDeletionRequested, "data_deletion_requested")]
    public void TypeNamesAreSnakeCaseAndKnown(OpsNotificationType type, string expected)
    {
        OpsNotificationTypes.Name(type).Should().Be(expected);
        OpsNotificationTypes.IsKnown(expected).Should().BeTrue();
        OpsNotificationTypes.IsKnown("TicketNew").Should().BeFalse();
        OpsNotificationTypes.IsKnown(null).Should().BeFalse();
    }

    [Fact]
    public async Task SendsOnlyToRolesThatHoldTheCapability()
    {
        await NotifyTicketAsync();

        _added.Select(n => n.UserId).Should().BeEquivalentTo([_manager, _admin]);
        _repository.Verify(r => r.ListActiveUserIdsByRolesAsync(
            It.Is<IReadOnlyCollection<UserRole>>(roles => roles.Contains(UserRole.OpsManager) && roles.Contains(UserRole.Admin) && !roles.Contains(UserRole.Staff)),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SkipsExcludedPeople()
    {
        await NotifyTicketAsync([_manager]);

        _added.Select(n => n.UserId).Should().BeEquivalentTo([_admin]);
    }

    [Fact]
    public async Task DoesNotStackAnotherRowWhileTheLastOneIsUnread()
    {
        _repository.Setup(r => r.ListUsersWithUnreadAsync(It.IsAny<OpsNotificationType>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { _manager });

        await NotifyTicketAsync();

        _added.Select(n => n.UserId).Should().BeEquivalentTo([_admin]);
    }

    [Fact]
    public async Task ADisabledTypeCreatesNothing()
    {
        UseSettings(new OpsNotificationSettings { DisabledTypes = ["ticket_new"] });

        await NotifyTicketAsync();

        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task TurningTheFeatureOffCreatesNothing()
    {
        UseSettings(new OpsNotificationSettings { Enabled = false });

        await NotifyTicketAsync();

        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task AUserWhoIsNoLongerActiveInternalGetsNothing()
    {
        await Service().NotifyUserAsync(OpsNotificationType.TicketAssigned, Guid.NewGuid(), "t", "b", "SupportTicket", Guid.NewGuid());

        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task AnActiveInternalUserGetsTheirOwnRow()
    {
        await Service().NotifyUserAsync(OpsNotificationType.TicketAssigned, _manager, "t", "b", "SupportTicket", Guid.NewGuid());

        _added.Should().ContainSingle().Which.UserId.Should().Be(_manager);
    }

    [Fact]
    public async Task AStorageFailureNeverReachesTheCaller()
    {
        _repository.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyList<OpsNotification>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        var act = () => NotifyTicketAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PurgeUsesTheConfiguredRetention()
    {
        UseSettings(new OpsNotificationSettings { RetentionDays = 10 });
        var now = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

        await Service().PurgeAsync(now);

        _repository.Verify(r => r.DeleteOlderThanAsync(now.AddDays(-10), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ASettingWithAnUnknownTypeFailsValidation()
    {
        var document = new SystemConfigDocument { OpsNotifications = new OpsNotificationSettings { DisabledTypes = ["ticket_new", "nope"] } };

        var result = await new SystemConfigDocumentValidator().ValidateAsync(document);

        result.Errors.Should().Contain(e => e.PropertyName.Contains("DisabledTypes"));
    }
}
