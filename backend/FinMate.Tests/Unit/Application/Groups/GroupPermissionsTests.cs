using FinMate.Application.Groups;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Groups;

public class GroupPermissionsTests
{
    private const GroupTransactionVisibility Full = GroupTransactionVisibility.Full;
    private const GroupTransactionVisibility Summary = GroupTransactionVisibility.Summary;

    private static GroupPermission For(GroupRole role, GroupTransactionVisibility visibility = Full, GroupPermission granted = GroupPermission.None, GroupPermission revoked = GroupPermission.None)
        => GroupPermissions.Effective(role, visibility, granted, revoked);

    [Theory]
    [InlineData(GroupRole.Owner, GroupPermission.RemoveMembers, true)]
    [InlineData(GroupRole.Manager, GroupPermission.ManageBudgets, true)]
    [InlineData(GroupRole.Manager, GroupPermission.InviteMembers, true)]
    [InlineData(GroupRole.Member, GroupPermission.ShareTransactions, true)]
    [InlineData(GroupRole.Member, GroupPermission.ContributeGoals, true)]
    [InlineData(GroupRole.Member, GroupPermission.ViewActivity, true)]
    [InlineData(GroupRole.Member, GroupPermission.ManageBudgets, false)]
    [InlineData(GroupRole.Member, GroupPermission.ManageSharedTransactions, false)]
    [InlineData(GroupRole.Member, GroupPermission.InviteMembers, false)]
    [InlineData(GroupRole.Viewer, GroupPermission.ViewOverview, true)]
    [InlineData(GroupRole.Viewer, GroupPermission.ViewActivity, false)]
    [InlineData(GroupRole.Viewer, GroupPermission.ShareTransactions, false)]
    [InlineData(GroupRole.Viewer, GroupPermission.ContributeGoals, false)]
    public void RoleDefaultsFollowTheMatrix(GroupRole role, GroupPermission permission, bool expected)
        => For(role).HasFlag(permission).Should().Be(expected);

    [Theory]
    [InlineData(GroupRole.Member)]
    [InlineData(GroupRole.Viewer)]
    public void SummaryVisibilityHidesTransactionDetails_FromMembersAndViewers(GroupRole role)
    {
        For(role, Full).HasFlag(GroupPermission.ViewMemberTransactions).Should().BeTrue();
        For(role, Summary).HasFlag(GroupPermission.ViewMemberTransactions).Should().BeFalse();
    }

    [Theory]
    [InlineData(GroupRole.Owner)]
    [InlineData(GroupRole.Manager)]
    public void SummaryVisibilityDoesNotAffectOwnersAndManagers(GroupRole role)
        => For(role, Summary).HasFlag(GroupPermission.ViewMemberTransactions).Should().BeTrue();

    [Fact]
    public void AGrantAddsAndARevokeRemoves_AndARevokeWinsOverAGrant()
    {
        For(GroupRole.Member, granted: GroupPermission.ManageBudgets).HasFlag(GroupPermission.ManageBudgets).Should().BeTrue();
        For(GroupRole.Member, revoked: GroupPermission.ShareTransactions).HasFlag(GroupPermission.ShareTransactions).Should().BeFalse();
        For(GroupRole.Member, granted: GroupPermission.ManageGoals, revoked: GroupPermission.ManageGoals).HasFlag(GroupPermission.ManageGoals).Should().BeFalse();
        For(GroupRole.Member, Summary, granted: GroupPermission.ViewMemberTransactions).HasFlag(GroupPermission.ViewMemberTransactions).Should().BeTrue();
    }

    [Fact]
    public void AnOwnerCannotBeReducedByOverrides()
        => For(GroupRole.Owner, revoked: GroupPermissions.All).Should().Be(GroupPermissions.All);

    [Fact]
    public void RankGoesOwnerManagerMemberViewer()
    {
        GroupPermissions.Outranks(GroupRole.Owner, GroupRole.Manager).Should().BeTrue();
        GroupPermissions.Outranks(GroupRole.Manager, GroupRole.Member).Should().BeTrue();
        GroupPermissions.Outranks(GroupRole.Manager, GroupRole.Manager).Should().BeFalse();
        GroupPermissions.Outranks(GroupRole.Member, GroupRole.Viewer).Should().BeTrue();
        GroupPermissions.Outranks(GroupRole.Manager, GroupRole.Owner).Should().BeFalse();
    }

    [Fact]
    public void CombineDropsBitsThatAreNotPermissions()
        => GroupPermissions.Combine([(GroupPermission)(1 << 20), GroupPermission.ManageGoals]).Should().Be(GroupPermission.ManageGoals);

    [Fact]
    public void ExpandListsEachPermissionOnce()
        => GroupPermissions.Expand(GroupPermission.ManageGoals | GroupPermission.ViewOverview)
            .Should().BeEquivalentTo([GroupPermission.ViewOverview, GroupPermission.ManageGoals]);
}
