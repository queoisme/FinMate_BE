using System.Net;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, ghi dòng quỹ qua API thật: BR-FUND-02 (số dư, âm được), 04 (quyền, đứng tên mình), 06 (chi có danh mục), 07 (kèm ví), 08 (giao dịch
/// cá nhân là Transfer gắn quỹ), 10/11 (chi tính cho nhóm, không cho cá nhân), D3, D10, D15, D16, D19.
/// </summary>
[Collection("Integration")]
public class GroupFundEntriesControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundEntriesControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record BudgetData(Guid Id, long SpentCents, long LimitCents);

    private async Task<(GroupFundApi.User Owner, Guid GroupId, Guid FundId)> FundAsync()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        return (owner, groupId, fund.Id);
    }

    [Fact]
    public async Task AContributionFromAWallet_MovesMoneyIntoTheFund_AndIsNeitherSpendingNorIncome()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var before = await _api.MonthlySummaryAsync(owner);

        var result = await _api.ContributeAsync(owner, groupId, fundId, 500_000);

        result.Fund.BalanceCents.Should().Be(500_000);
        result.Entry.Kind.Should().Be("Contribution");
        result.Entry.MemberUserId.Should().Be(owner.Id);
        result.Entry.HasPersonalLink.Should().BeTrue();
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000);

        var link = await _api.PersonalLinkAsync(result.Entry.Id);
        link.Should().NotBeNull();
        link!.TransactionType.Should().Be(TransactionType.Transfer, "BR-FUND-08");
        link.CounterAccountId.Should().BeNull();
        link.GroupId.Should().BeNull("không chia sẻ nhóm được");
        link.CategoryId.Should().BeNull();

        var after = await _api.MonthlySummaryAsync(owner);
        after.Should().Be(before, "BR-FUND-08/11: góp quỹ không phải chi, không phải thu");
    }

    [Fact]
    public async Task AContributionWithoutAWallet_OnlyTouchesTheFund()
    {
        var (owner, groupId, fundId) = await FundAsync();

        var result = await _api.ContributeAsync(owner, groupId, fundId, 300_000, fromWallet: false);

        result.Fund.BalanceCents.Should().Be(300_000);
        result.Entry.HasPersonalLink.Should().BeFalse();
        (await _api.PersonalLinkAsync(result.Entry.Id)).Should().BeNull();
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance);
    }

    [Fact]
    public async Task AWithdrawalIntoAWallet_AddsToTheWallet_AndTheFundMayGoNegative()
    {
        var (owner, groupId, fundId) = await FundAsync();

        var result = await _api.RecordAsync(owner, groupId, fundId, new { kind = "Withdrawal", amountCents = 200_000L, financialAccountId = owner.AccountId });

        result.Fund.BalanceCents.Should().Be(-200_000);
        result.Fund.IsNegative.Should().BeTrue("BR-FUND-02: âm được, có cờ");
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance + 200_000);
        (await _api.PersonalLinkAsync(result.Entry.Id))!.TransactionType.Should().Be(TransactionType.Transfer);
    }

    [Fact]
    public async Task AFundExpense_CountsForTheGroupBudget_NotForTheRecordersOwnSpending()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var budget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });
        await _api.ContributeAsync(member, groupId, fundId, 1_000_000);
        var before = await _api.MonthlySummaryAsync(member);

        var result = await _api.SpendAsync(member, groupId, fundId, 800_000, bills);

        result.Fund.BalanceCents.Should().Be(200_000);
        result.Entry.CategoryName.Should().NotBeNullOrEmpty();
        var budgets = await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token);
        budgets.Single(b => b.Id == budget.Id).SpentCents.Should().Be(800_000, "BR-FUND-10");
        (await _api.MonthlySummaryAsync(member)).Should().Be(before, "BR-FUND-11: không vào sổ chi tiêu của người ghi");
    }

    [Fact]
    public async Task AFundExpensePaidFromAWallet_TakesTheWallet_ButStaysOutOfPersonalSpending()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var before = await _api.MonthlySummaryAsync(owner);

        var result = await _api.SpendAsync(owner, groupId, fundId, 400_000, bills, accountId: owner.AccountId);

        result.Fund.BalanceCents.Should().Be(-400_000);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 400_000, "S4b: giao dịch transfer gắn quỹ chiều ra");
        (await _api.MonthlySummaryAsync(owner)).Should().Be(before);
    }

    [Theory]
    [InlineData("Member", HttpStatusCode.Created)]
    [InlineData("Manager", HttpStatusCode.Created)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    public async Task RecordingNeedsRecordFundEntries(string role, HttpStatusCode expected)
    {
        var (owner, groupId, fundId) = await FundAsync();
        var user = await _api.JoinAsync(owner, groupId, role);

        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), user.Token, expected, new { kind = "Contribution", amountCents = 100_000L });
    }

    [Fact]
    public async Task RecordFundEntries_FollowsOverrides()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var viewer = await _api.JoinAsync(owner, groupId, "Viewer");
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{member.Id}", owner.Token, new { revokedPermissions = new[] { "RecordFundEntries" } });
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}/members/{viewer.Id}", owner.Token, new { grantedPermissions = new[] { "RecordFundEntries" } });
        var body = new { kind = "Contribution", amountCents = 100_000L };

        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), member.Token, HttpStatusCode.Forbidden, body);
        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), viewer.Token, HttpStatusCode.Created, body);
    }

    [Fact]
    public async Task Adjustments_NeedManageFunds_AndANote_AndMayBeNegative()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var url = GroupFundApi.Entries(groupId, fundId);

        await _api.ExpectAsync(HttpMethod.Post, url, member.Token, HttpStatusCode.Forbidden, new { kind = "Adjustment", amountCents = -50_000L, note = "Đếm lại" });
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.BadRequest, new { kind = "Adjustment", amountCents = -50_000L });
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Adjustment", amountCents = 50_000L, note = "Đếm lại", financialAccountId = owner.AccountId }, "GROUP_FUND_ENTRY_INVALID");

        var result = await _api.RecordAsync(owner, groupId, fundId, new { kind = "Adjustment", amountCents = -50_000L, note = "Đếm lại tiền mặt" });
        result.Fund.BalanceCents.Should().Be(-50_000);
    }

    [Fact]
    public async Task ExpenseShape_IsChecked()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var url = GroupFundApi.Entries(groupId, fundId);
        var loan = await _api.SystemCategoryAsync(owner.Token, "loan");
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var mine = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/categories", owner.Token, new { name = "Riêng tôi", iconName = "star" });

        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.UnprocessableEntity, new { kind = "Expense", amountCents = 1L }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.UnprocessableEntity, new { kind = "Expense", amountCents = 1L, categoryId = loan }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.UnprocessableEntity, new { kind = "Expense", amountCents = 1L, categoryId = mine.Id }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.UnprocessableEntity, new { kind = "Contribution", amountCents = 1L, categoryId = bills }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.NotFound, new { kind = "Expense", amountCents = 1L, groupCategoryId = Guid.NewGuid() });
        await _api.ExpectAsync(HttpMethod.Post, url, owner.Token, HttpStatusCode.BadRequest, new { kind = "Contribution", amountCents = 0L });
    }

    [Fact]
    public async Task SomeoneElsesWallet_IsNotFound_AndNothingIsSaved()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);

        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), member.Token, HttpStatusCode.NotFound,
            new { kind = "Contribution", amountCents = 100_000L, financialAccountId = owner.AccountId });

        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(0);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance);
    }

    [Fact]
    public async Task AnArchivedFund_TakesNoNewEntries()
    {
        var (owner, groupId, fundId) = await FundAsync();
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{GroupFundApi.Funds(groupId)}/{fundId}/archive", owner.Token);

        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), owner.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Contribution", amountCents = 100_000L }, "GROUP_FUND_ARCHIVED");
    }

    [Fact]
    public async Task ResendingWithTheSameClientRequestId_ReturnsTheSameEntry_AndCountsOnce()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var body = new { kind = "Contribution", amountCents = 500_000L, financialAccountId = owner.AccountId, clientRequestId = Guid.NewGuid() };

        var first = await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), owner.Token, HttpStatusCode.Created, body);
        var second = await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(groupId, fundId), owner.Token, HttpStatusCode.OK, body);

        (await Read(first)).Entry.Id.Should().Be((await Read(second)).Entry.Id);
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(500_000);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000);
    }

    [Fact]
    public async Task TheLedger_IsVisibleToEveryMember_EvenInSummaryMode_ButPersonalLinksOnlyToTheirOwner()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var viewer = await _api.JoinAsync(owner, groupId, "Viewer");
        await _api.ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}", owner.Token, new { transactionVisibility = "Summary" });
        await _api.ContributeAsync(owner, groupId, fundId, 500_000);
        await _api.ContributeAsync(member, groupId, fundId, 300_000, fromWallet: false);

        var seenByViewer = await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, GroupFundApi.Entries(groupId, fundId), viewer.Token);
        var seenByOwner = await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, GroupFundApi.Entries(groupId, fundId), owner.Token);

        seenByViewer.Should().HaveCount(2, "D15: sổ quỹ là của nhóm");
        seenByViewer.Should().OnlyContain(e => e.HasPersonalLink == null, "không ai thấy sổ cá nhân của người khác");
        seenByOwner.Single(e => e.MemberUserId == owner.Id).HasPersonalLink.Should().BeTrue();
        seenByOwner.Single(e => e.MemberUserId == member.Id).HasPersonalLink.Should().BeNull();
    }

    [Fact]
    public async Task TheLedger_FiltersByKindAndMember_AndPagesWithACursor()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        await _api.ContributeAsync(owner, groupId, fundId, 100_000, fromWallet: false);
        await _api.ContributeAsync(member, groupId, fundId, 200_000, fromWallet: false);
        await _api.SpendAsync(member, groupId, fundId, 50_000, bills);
        var url = GroupFundApi.Entries(groupId, fundId);

        (await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, $"{url}?kind=Contribution", owner.Token)).Should().HaveCount(2);
        (await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, $"{url}?memberId={member.Id}", owner.Token)).Should().HaveCount(2);

        var firstPage = await _api.SendAsync(HttpMethod.Get, $"{url}?limit=2", owner.Token);
        var page = System.Text.Json.JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()).RootElement;
        page.GetProperty("data").GetArrayLength().Should().Be(2);
        var cursor = page.GetProperty("meta").GetProperty("cursor").GetString();
        cursor.Should().NotBeNull();
        (await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, $"{url}?limit=2&cursor={Uri.EscapeDataString(cursor!)}", owner.Token)).Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordingWritesActivities_ForContributionsAndWithdrawalsOnly()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var contribution = await _api.ContributeAsync(owner, groupId, fundId, 100_000, fromWallet: false);
        var withdrawal = await _api.RecordAsync(owner, groupId, fundId, new { kind = "Withdrawal", amountCents = 50_000L });
        var expense = await _api.SpendAsync(owner, groupId, fundId, 10_000, bills);

        var activities = await _api.WithDbAsync(db => db.GroupActivities.Where(a => a.GroupId == groupId).Select(a => new { a.Type, a.SubjectId }).ToListAsync());

        activities.Should().Contain(a => a.Type == GroupActivityType.FundContributed && a.SubjectId == contribution.Entry.Id);
        activities.Should().Contain(a => a.Type == GroupActivityType.FundWithdrawn && a.SubjectId == withdrawal.Entry.Id);
        activities.Should().NotContain(a => a.SubjectId == expense.Entry.Id);
    }

    private static async Task<GroupFundApi.EntryResult> Read(HttpResponseMessage response)
        => (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<GroupFundApi.Wrapper<GroupFundApi.EntryResult>>(response.Content, GroupFundApi.JsonOptions))!.Data;
}
