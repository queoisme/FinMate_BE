using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, BR-FUND-16, D18: quỹ chuyển sang âm thì push một lần cho người có <c>ManageFunds</c> (không số tiền); âm thêm không báo nữa; về ≥ 0 rồi âm
/// lại thì báo lại. Người tắt cảnh báo nhóm, người không quản lý quỹ, và lúc cờ tắt thì không nhận.
/// </summary>
[Collection("Integration")]
public class GroupFundNegativeAlertTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundNegativeAlertTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    private Task<List<ForecastAlert>> AlertsAsync(Guid fundId)
        => _api.WithDbAsync(db => db.ForecastAlerts.AsNoTracking()
            .Where(a => a.Kind == ForecastAlertKind.GroupFundNegative && a.DedupKey.StartsWith("group_fund_negative:" + fundId))
            .ToListAsync());

    [Fact]
    public async Task GoingNegative_AlertsFundManagersOnce_AndAgainAfterRecovering()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var manager = await _api.JoinAsync(owner, groupId, "Manager");
        var member = await _api.JoinAsync(owner, groupId);
        var promoted = await _api.JoinAsync(owner, groupId);
        var muted = await _api.JoinAsync(owner, groupId, "Manager");
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{promoted.Id}", owner.Token, new { grantedPermissions = new[] { "ManageFunds" } });
        (await _api.SendAsync(HttpMethod.Put, $"/api/v1/groups/{groupId}/alerts", muted.Token, new { enabled = false })).EnsureSuccessStatusCode();
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        await _api.ContributeAsync(member, groupId, fund.Id, 700_000, fromWallet: false);

        await _api.SpendAsync(member, groupId, fund.Id, 900_000, bills);      // 700k → −200k: báo
        await _api.SpendAsync(member, groupId, fund.Id, 100_000, bills);      // −200k → −300k: không báo nữa

        var first = await AlertsAsync(fund.Id);
        first.Select(a => a.UserId).Should().BeEquivalentTo(new[] { owner.Id, manager.Id, promoted.Id }, "chỉ người có ManageFunds và đang bật cảnh báo");
        first.Should().OnlyContain(a => !a.Body.Contains("200") && !a.Body.Contains("900") && !a.Title.Contains("000"), "không số tiền");

        await _api.ContributeAsync(member, groupId, fund.Id, 500_000, fromWallet: false);   // −300k → 200k
        await _api.SpendAsync(member, groupId, fund.Id, 400_000, bills);                    // 200k → −200k: báo lại

        var all = await AlertsAsync(fund.Id);
        all.Should().HaveCount(6, "hai đợt âm × ba người");
        all.Select(a => a.DedupKey).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task WithTheFlagOff_GoingNegativeThroughAPersonalDelete_SendsNothing()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var contribution = (await _api.ContributeAsync(member, groupId, fund.Id, 500_000)).Entry;
        await _api.SpendAsync(member, groupId, fund.Id, 300_000, bills);
        var txId = (await _api.PersonalLinkAsync(contribution.Id))!.Id;
        await _factory.SetFeaturesAsync(groupFunds: false);

        (await _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{txId}", member.Token)).EnsureSuccessStatusCode();

        var balance = await _api.WithDbAsync(db => db.GroupFunds.AsNoTracking().Where(f => f.Id == fund.Id).Select(f => f.BalanceCents).SingleAsync());
        balance.Should().Be(-300_000);
        (await AlertsAsync(fund.Id)).Should().BeEmpty("cờ tắt thì không gửi");
    }

    [Fact]
    public async Task GoingNegativeThroughAPersonalDelete_Alerts()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var contribution = (await _api.ContributeAsync(member, groupId, fund.Id, 500_000)).Entry;
        await _api.SpendAsync(member, groupId, fund.Id, 300_000, bills);
        var txId = (await _api.PersonalLinkAsync(contribution.Id))!.Id;

        (await _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{txId}", member.Token)).EnsureSuccessStatusCode();

        (await AlertsAsync(fund.Id)).Select(a => a.UserId).Should().Equal(owner.Id);
    }
}
