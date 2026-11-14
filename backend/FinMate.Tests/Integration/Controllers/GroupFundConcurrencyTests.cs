using System.Net;
using FinMate.Application.Groups.Funds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, D2: nhiều người ghi cùng một quỹ cùng lúc. Số dư quỹ lưu sẵn có <c>xmin</c>; lượt thua được chạy lại trên số mới, nên không dòng nào bị
/// mất và số dư luôn bằng Σ dòng. Ví và kỳ ngân sách nhóm cũng vậy.
/// </summary>
[Collection("Integration")]
public class GroupFundConcurrencyTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundConcurrencyTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record BudgetData(Guid Id, long SpentCents);

    private async Task<(long Balance, long Sum, int Count)> FundStateAsync(Guid fundId)
        => await _api.WithDbAsync(async db =>
        {
            var balance = await db.GroupFunds.AsNoTracking().Where(f => f.Id == fundId).Select(f => f.BalanceCents).SingleAsync();
            var entries = await db.GroupFundEntries.AsNoTracking().Where(e => e.FundId == fundId && e.DeletedAt == null).ToListAsync();
            return (balance, entries.Sum(GroupFundMath.Signed), entries.Count);
        });

    [Fact]
    public async Task ThreePeopleContributingAtOnce_AllCount()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var b = await _api.JoinAsync(owner, groupId);
        var c = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);

        var responses = await Task.WhenAll(new[] { owner, b, c }.Select(u =>
            _api.SendAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), u.Token, new { kind = "Contribution", amountCents = 500_000L, financialAccountId = u.AccountId })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var (balance, sum, count) = await FundStateAsync(fund.Id);
        balance.Should().Be(1_500_000);
        sum.Should().Be(balance);
        count.Should().Be(3);
        foreach (var user in new[] { owner, b, c })
        {
            (await _api.BalanceAsync(user.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000);
        }
    }

    [Fact]
    public async Task TwoExpensesAtOnce_BothReachTheFundAndTheGroupBudget()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var budget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });

        var responses = await Task.WhenAll(
            _api.SendAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), owner.Token, new { kind = "Expense", amountCents = 300_000L, categoryId = bills }),
            _api.SendAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), member.Token, new { kind = "Expense", amountCents = 400_000L, categoryId = bills }));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var (balance, sum, _) = await FundStateAsync(fund.Id);
        balance.Should().Be(-700_000);
        sum.Should().Be(balance);
        var budgets = await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token);
        budgets.Single(x => x.Id == budget.Id).SpentCents.Should().Be(700_000);
    }

    [Fact]
    public async Task ResendingTheSameEntryAtOnce_CreatesItOnce()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        var body = new { kind = "Contribution", amountCents = 500_000L, financialAccountId = owner.AccountId, clientRequestId = Guid.NewGuid() };

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _api.SendAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), owner.Token, body)));

        responses.Should().OnlyContain(r => r.IsSuccessStatusCode);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var (balance, _, count) = await FundStateAsync(fund.Id);
        count.Should().Be(1);
        balance.Should().Be(500_000);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000);
    }

    [Fact]
    public async Task EditingAndContributingAtOnce_StayConsistent()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        var entry = (await _api.ContributeAsync(owner, groupId, fund.Id, 500_000)).Entry;

        var responses = await Task.WhenAll(
            _api.SendAsync(HttpMethod.Patch, $"{GroupFundApi.Entries(groupId, fund.Id)}/{entry.Id}", owner.Token, new { amountCents = 800_000L }),
            _api.SendAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fund.Id), member.Token, new { kind = "Contribution", amountCents = 200_000L }));

        responses.Should().OnlyContain(r => r.IsSuccessStatusCode);
        var (balance, sum, _) = await FundStateAsync(fund.Id);
        balance.Should().Be(1_000_000);
        sum.Should().Be(balance);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 800_000);
    }
}
