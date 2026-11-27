using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application;

public class AgentContentCapabilityTests
{
    [Fact]
    public void Defaults_StaffDrafts_ManagerAndAdminAlsoApprove()
    {
        OpsPermissions.For(UserRole.Staff).Should().HaveFlag(OpsCapability.ManageAgentContent).And.NotHaveFlag(OpsCapability.ApproveAgentContent);
        OpsPermissions.For(UserRole.OpsManager).Should().HaveFlag(OpsCapability.ManageAgentContent).And.HaveFlag(OpsCapability.ApproveAgentContent);
        OpsPermissions.For(UserRole.Admin).Should().HaveFlag(OpsCapability.ApproveAgentContent);
    }

    [Fact]
    public void Approve_WithoutDraft_IsReportedAsBrokenDependency()
        => RolePermissionRules.BrokenDependencies(OpsCapability.ApproveAgentContent).Should().ContainSingle();

    [Fact]
    public void BitsMatchTheMigrationConstants()
    {
        ((long)OpsCapability.ManageAgentContent).Should().Be(8388608);
        ((long)OpsCapability.ApproveAgentContent).Should().Be(4194304);
    }
}
