using System.Net;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 85, BR-FUND-01..03, D16, D17: tạo, sửa, liệt kê, lưu trữ quỹ nhóm qua API thật, theo vai trò và quyền ghi đè.</summary>
[Collection("Integration")]
public class GroupFundsControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Funds(Guid groupId) => GroupFundApi.Funds(groupId);

    [Fact]
    public async Task OwnerCreatesAFund_EveryMemberSeesIt_StartingAtZero()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var viewer = await _api.JoinAsync(owner, groupId, "Viewer");

        var created = await _api.CreateFundAsync(owner, groupId, "  Tiền điện nước  ", "Góp đầu tháng");

        created.Name.Should().Be("Tiền điện nước");
        created.BalanceCents.Should().Be(0);
        created.IsNegative.Should().BeFalse();
        var list = await _api.ReadAsync<List<GroupFundApi.FundData>>(HttpMethod.Get, Funds(groupId), viewer.Token);
        list.Should().ContainSingle(f => f.Id == created.Id && f.Note == "Góp đầu tháng");
        var activity = await _api.WithDbAsync(db => db.GroupActivities.AnyAsync(a => a.GroupId == groupId && a.Type == GroupActivityType.FundCreated && a.SubjectId == created.Id));
        activity.Should().BeTrue();
    }

    [Theory]
    [InlineData("Manager", HttpStatusCode.Created)]
    [InlineData("Member", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    public async Task CreatingAFund_NeedsManageFunds(string role, HttpStatusCode expected)
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var user = await _api.JoinAsync(owner, groupId, role);

        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), user.Token, expected, new { name = "Quỹ du lịch" });
    }

    [Fact]
    public async Task ManageFunds_FollowsPerPersonOverrides()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var manager = await _api.JoinAsync(owner, groupId, "Manager");
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{member.Id}", owner.Token, new { grantedPermissions = new[] { "ManageFunds" } });
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{manager.Id}", owner.Token, new { revokedPermissions = new[] { "ManageFunds" } });

        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), member.Token, HttpStatusCode.Created, new { name = "Quỹ chợ" });
        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), manager.Token, HttpStatusCode.Forbidden, new { name = "Quỹ khác" });
    }

    [Fact]
    public async Task ANonMember_GetsNotFound()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        var stranger = await _api.NewUserAsync();

        await _api.ExpectAsync(HttpMethod.Get, Funds(groupId), stranger.Token, HttpStatusCode.NotFound);
        await _api.ExpectAsync(HttpMethod.Get, $"{Funds(groupId)}/{fund.Id}", stranger.Token, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AGroupHasAtMostThreeActiveFunds_ArchivedOnesDoNotCount()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var first = await _api.CreateFundAsync(owner, groupId, "Một");
        await _api.CreateFundAsync(owner, groupId, "Hai");
        await _api.CreateFundAsync(owner, groupId, "Ba");

        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), owner.Token, HttpStatusCode.UnprocessableEntity, new { name = "Bốn" }, "GROUP_TOO_MANY_FUNDS");

        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{Funds(groupId)}/{first.Id}/archive", owner.Token);
        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), owner.Token, HttpStatusCode.Created, new { name = "Bốn" });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AFundNeedsAName(string name)
    {
        var (owner, groupId) = await _api.NewGroupAsync();

        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), owner.Token, HttpStatusCode.BadRequest, new { name });
        await _api.ExpectAsync(HttpMethod.Post, Funds(groupId), owner.Token, HttpStatusCode.BadRequest, new { name = new string('x', 101) });
    }

    [Fact]
    public async Task RenamingAndClearingTheNote_WorksEvenWhenArchived()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId, "Cũ", "Ghi chú");
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{Funds(groupId)}/{fund.Id}/archive", owner.Token);

        var updated = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Patch, $"{Funds(groupId)}/{fund.Id}", owner.Token, new { name = "Mới", note = "" });

        updated.Name.Should().Be("Mới");
        updated.Note.Should().BeNull();
        updated.IsArchived.Should().BeTrue();
    }

    [Theory]
    [InlineData(-200_000L)]
    [InlineData(300_000L)]
    public async Task ArchivingNeedsAZeroBalance(long balance)
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        await _api.WithDbAsync(db => db.GroupFunds.Where(f => f.Id == fund.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.BalanceCents, balance)));

        await _api.ExpectAsync(HttpMethod.Post, $"{Funds(groupId)}/{fund.Id}/archive", owner.Token, HttpStatusCode.UnprocessableEntity, code: "GROUP_FUND_NOT_SETTLED");

        (await _api.GetFundAsync(owner, groupId, fund.Id)).IsNegative.Should().Be(balance < 0);
    }

    [Fact]
    public async Task ArchivingTwice_IsHarmless()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);

        var first = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{Funds(groupId)}/{fund.Id}/archive", owner.Token);
        var second = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{Funds(groupId)}/{fund.Id}/archive", owner.Token);

        second.ArchivedAt.Should().BeCloseTo(first.ArchivedAt!.Value, TimeSpan.FromMilliseconds(1), "lưu trữ lần hai không đổi thời điểm");
    }

    [Fact]
    public async Task DeletingAGroup_WithAnActiveFund_IsRefused_UntilItIsArchived()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);

        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}", owner.Token, HttpStatusCode.UnprocessableEntity, code: "GROUP_FUND_NOT_SETTLED");

        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{Funds(groupId)}/{fund.Id}/archive", owner.Token);
        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}", owner.Token, HttpStatusCode.NoContent);
    }
}
