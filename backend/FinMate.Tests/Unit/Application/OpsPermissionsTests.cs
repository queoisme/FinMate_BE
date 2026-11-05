using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application;

public class OpsPermissionsTests
{
    [Theory]
    [InlineData(UserRole.Staff, OpsCapability.HandleTickets, true)]
    [InlineData(UserRole.Staff, OpsCapability.AccessCustomerData, true)]
    [InlineData(UserRole.Staff, OpsCapability.CreateDraftTransactions, true)]
    [InlineData(UserRole.Staff, OpsCapability.EmergencyLock, true)]
    [InlineData(UserRole.Staff, OpsCapability.ProposeApprovals, true)]
    [InlineData(UserRole.Staff, OpsCapability.ManageTickets, false)]
    [InlineData(UserRole.Staff, OpsCapability.DecideApprovals, false)]
    [InlineData(UserRole.Staff, OpsCapability.ViewDashboard, false)]
    [InlineData(UserRole.Staff, OpsCapability.ManageCannedReplies, false)]
    [InlineData(UserRole.Staff, OpsCapability.ViewOpsAudit, false)]
    [InlineData(UserRole.Staff, OpsCapability.ViewAiStats, false)]
    [InlineData(UserRole.Staff, OpsCapability.ManageSystem, false)]
    [InlineData(UserRole.OpsManager, OpsCapability.HandleTickets, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ManageTickets, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.DecideApprovals, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ViewDashboard, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ManageCannedReplies, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ViewOpsAudit, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ViewAiStats, true)]
    [InlineData(UserRole.OpsManager, OpsCapability.ManageSystem, false)]
    [InlineData(UserRole.Admin, OpsCapability.HandleTickets, true)]
    [InlineData(UserRole.Admin, OpsCapability.DecideApprovals, true)]
    [InlineData(UserRole.Admin, OpsCapability.ManageSystem, true)]
    [InlineData(UserRole.User, OpsCapability.HandleTickets, false)]
    public void MatrixMatchesTheOperatingModel(UserRole role, OpsCapability capability, bool expected)
        => OpsPermissions.For(role).HasFlag(capability).Should().Be(expected);

    [Fact]
    public void AnOrdinaryUserHasNoCapabilityAtAll()
        => OpsPermissions.For(UserRole.User).Should().Be(OpsCapability.None);

    [Fact]
    public void NamesListsOnlyWhatWasGranted()
        => OpsPermissions.Names(OpsCapability.HandleTickets | OpsCapability.ViewDashboard)
            .Should().BeEquivalentTo("HandleTickets", "ViewDashboard");

    [Theory]
    [InlineData(UserRole.Admin, UserRole.OpsManager)]
    [InlineData(UserRole.Admin, UserRole.Staff)]
    [InlineData(UserRole.OpsManager, UserRole.Staff)]
    [InlineData(UserRole.Staff, UserRole.User)]
    public void RankDescendsWithPrivilege(UserRole higher, UserRole lower)
        => higher.Rank().Should().BeGreaterThan(lower.Rank());
}
