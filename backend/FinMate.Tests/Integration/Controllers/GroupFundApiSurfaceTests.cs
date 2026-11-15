using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, Task 16: bề mặt API mà app thấy. Hai quyền mới trong <c>myPermissions</c>; <c>groupFundLink</c> trong giao dịch cá nhân (cả sau khi rời nhóm,
/// D20, và khi dòng đã bị quản lý xoá, S3); hai cờ chặn ở server; <c>features.groupFunds</c> trong <c>/app-config</c>.
/// </summary>
[Collection("Integration")]
public class GroupFundApiSurfaceTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundApiSurfaceTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    private sealed record GroupData(List<string> MyPermissions);
    private sealed record FundLink(Guid GroupId, Guid FundId, string FundName, string Kind, string Direction, bool EntryDeleted);
    private sealed record TxData(Guid Id, string TransactionType, Guid? CounterAccountId, FundLink? GroupFundLink);

    [Theory]
    [InlineData("Manager", true, true)]
    [InlineData("Member", false, true)]
    [InlineData("Viewer", false, false)]
    public async Task MyPermissions_ListTheFundPermissions(string role, bool manages, bool records)
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var user = await _api.JoinAsync(owner, groupId, role);

        var mine = (await _api.ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{groupId}", user.Token)).MyPermissions;
        var owners = (await _api.ReadAsync<GroupData>(HttpMethod.Get, $"/api/v1/groups/{groupId}", owner.Token)).MyPermissions;

        mine.Contains("ManageFunds").Should().Be(manages);
        mine.Contains("RecordFundEntries").Should().Be(records);
        owners.Should().Contain(["ManageFunds", "RecordFundEntries"]);
    }

    [Fact]
    public async Task APersonalFundTransaction_CarriesItsFundLink_InDetailAndList()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId, "Quỹ du lịch");
        var contribution = (await _api.ContributeAsync(owner, groupId, fund.Id, 500_000)).Entry;
        var withdrawal = (await _api.RecordAsync(owner, groupId, fund.Id, new { kind = "Withdrawal", amountCents = 100_000L, financialAccountId = owner.AccountId })).Entry;
        var contributionTx = (await _api.PersonalLinkAsync(contribution.Id))!.Id;
        var withdrawalTx = (await _api.PersonalLinkAsync(withdrawal.Id))!.Id;

        var detail = await _api.ReadAsync<TxData>(HttpMethod.Get, $"/api/v1/transactions/{contributionTx}", owner.Token);
        detail.TransactionType.Should().Be("Transfer");
        detail.CounterAccountId.Should().BeNull();
        detail.GroupFundLink.Should().Be(new FundLink(groupId, fund.Id, "Quỹ du lịch", "Contribution", "out", false));

        var list = await _api.ReadAsync<List<TxData>>(HttpMethod.Get, "/api/v1/transactions", owner.Token);
        list.Single(t => t.Id == withdrawalTx).GroupFundLink!.Direction.Should().Be("in");
        list.Single(t => t.Id == contributionTx).GroupFundLink!.Kind.Should().Be("Contribution");
    }

    [Fact]
    public async Task TheFundLink_StaysAfterLeaving_AndShowsWhenAManagerDeletedTheLine()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        var removed = (await _api.ContributeAsync(member, groupId, fund.Id, 200_000)).Entry;
        var kept = (await _api.ContributeAsync(member, groupId, fund.Id, 300_000)).Entry;
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, $"{GroupFundApi.Entries(groupId, fund.Id)}/{removed.Id}", owner.Token);
        (await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", member.Token)).EnsureSuccessStatusCode();

        var removedTx = await _api.ReadAsync<TxData>(HttpMethod.Get, $"/api/v1/transactions/{(await _api.PersonalLinkAsync(removed.Id))!.Id}", member.Token);
        var keptTx = await _api.ReadAsync<TxData>(HttpMethod.Get, $"/api/v1/transactions/{(await _api.PersonalLinkAsync(kept.Id))!.Id}", member.Token);

        removedTx.GroupFundLink!.EntryDeleted.Should().BeTrue("S3");
        keptTx.GroupFundLink!.EntryDeleted.Should().BeFalse();
        keptTx.GroupFundLink.FundId.Should().Be(fund.Id, "D20: lịch sử của chính họ");
    }

    [Fact]
    public async Task FundRoutes_AreBlockedByBothFlags_GroupsFirst()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        var routes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, GroupFundApi.Funds(groupId), null),
            (HttpMethod.Post, GroupFundApi.Funds(groupId), new { name = "Mới" }),
            (HttpMethod.Get, $"{GroupFundApi.Funds(groupId)}/{fund.Id}/summary", null),
            (HttpMethod.Get, GroupFundApi.Entries(groupId, fund.Id), null),
            (HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), new { kind = "Contribution", amountCents = 1L }),
        };

        await _factory.SetFeaturesAsync(groupFunds: false);
        foreach (var (method, url, body) in routes)
        {
            await _api.ExpectAsync(method, url, owner.Token, HttpStatusCode.UnprocessableEntity, body, "GROUP_FUNDS_DISABLED");
        }

        await _factory.SetFeaturesAsync(groups: false, groupFunds: true);
        foreach (var (method, url, body) in routes)
        {
            await _api.ExpectAsync(method, url, owner.Token, HttpStatusCode.UnprocessableEntity, body, "GROUPS_DISABLED");
        }
    }

    [Fact]
    public async Task AppConfig_TellsTheAppWhetherFundsAreOn()
    {
        await _factory.SetFeaturesAsync(groupFunds: false);
        var off = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/v1/app-config")).RootElement;
        off.GetProperty("data").GetProperty("config").GetProperty("features").GetProperty("groupFunds").GetBoolean().Should().BeFalse();

        await _factory.SetFeaturesAsync(groupFunds: true);
        var on = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/v1/app-config")).RootElement;
        on.GetProperty("data").GetProperty("config").GetProperty("features").GetProperty("groupFunds").GetBoolean().Should().BeTrue();
    }
}
