using System.Net;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 85, BR-FUND-10/12, D14 (theo S5): chi từ quỹ trong báo cáo tháng của nhóm, và báo cáo của chính quỹ (đầu kỳ, góp, chi, rút, cuối kỳ).</summary>
[Collection("Integration")]
public class GroupFundReportTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundReportTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record CategorySpend(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long SpentCents);
    private sealed record MemberTotals(Guid UserId, string MemberName, long SpentCents, long IncomeCents, long FundSpentCents);
    private sealed record Former(long TotalSpentCents, long FundSpentCents);
    private sealed record Monthly(long TotalSpentCents, long TotalIncomeCents, List<CategorySpend> ByCategory, List<MemberTotals> ByMember, long FundSpentCents, Former? FormerMembers);
    private sealed record MemberAmount(Guid? MemberUserId, string MemberName, long AmountCents);
    private sealed record CategoryAmount(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long AmountCents);
    private sealed record Summary(
        long OpeningCents, long ContributedCents, List<MemberAmount> ContributionsByMember, long SpentCents, List<CategoryAmount> ExpensesByCategory,
        long WithdrawnCents, List<MemberAmount> WithdrawalsByMember, long AdjustmentsCents, long ClosingCents);

    [Fact]
    public async Task TheGroupMonthlyReport_AddsFundExpenses_UnderTheirRecorder_AndIgnoresContributions()
    {
        var (a, groupId) = await _api.NewGroupAsync(ownerName: "A");
        var b = await _api.JoinAsync(a, groupId, name: "B");
        var fund = await _api.CreateFundAsync(a, groupId);
        var food = await _api.SystemCategoryAsync(a.Token, "food");
        var bills = await _api.SystemCategoryAsync(a.Token, "bills");
        await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", a.Token, new
        {
            financialAccountId = a.AccountId, categoryId = food, amountCents = 150_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, groupId,
        });
        await _api.ContributeAsync(a, groupId, fund.Id, 500_000);
        await _api.SpendAsync(b, groupId, fund.Id, 800_000, bills);

        var report = await _api.ReadAsync<Monthly>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", a.Token);

        report.TotalSpentCents.Should().Be(950_000);
        report.TotalIncomeCents.Should().Be(0, "góp quỹ không phải thu");
        report.FundSpentCents.Should().Be(800_000);
        report.FormerMembers.Should().BeNull();
        report.ByCategory.Should().ContainSingle(c => c.CategoryId == bills && c.SpentCents == 800_000);
        report.ByCategory.Should().ContainSingle(c => c.CategoryId == food && c.SpentCents == 150_000);
        var byB = report.ByMember.Single(m => m.UserId == b.Id);
        byB.SpentCents.Should().Be(800_000, "byMember = người ghi");
        byB.FundSpentCents.Should().Be(800_000);
        report.ByMember.Single(m => m.UserId == a.Id).FundSpentCents.Should().Be(0);
    }

    [Fact]
    public async Task TheFundSummary_ShowsOpeningContributionsExpensesWithdrawalsAndClosing()
    {
        var (a, groupId) = await _api.NewGroupAsync(ownerName: "A");
        var b = await _api.JoinAsync(a, groupId, name: "B");
        var viewer = await _api.JoinAsync(a, groupId, "Viewer");
        var fund = await _api.CreateFundAsync(a, groupId);
        var bills = await _api.SystemCategoryAsync(a.Token, "bills");
        var (thisYear, thisMonth) = VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        var lastMonth = VietnamTime.MonthRange(thisYear, thisMonth).Start.AddDays(-3);

        await _api.RecordAsync(a, groupId, fund.Id, new { kind = "Contribution", amountCents = 1_000_000L, transactedAt = lastMonth });
        await _api.RecordAsync(b, groupId, fund.Id, new { kind = "Expense", amountCents = 300_000L, categoryId = bills, transactedAt = lastMonth });

        await _api.ContributeAsync(a, groupId, fund.Id, 500_000, fromWallet: false);
        await _api.ContributeAsync(b, groupId, fund.Id, 200_000, fromWallet: false);
        await _api.SpendAsync(b, groupId, fund.Id, 400_000, bills);
        await _api.RecordAsync(a, groupId, fund.Id, new { kind = "Withdrawal", amountCents = 100_000L });
        await _api.RecordAsync(a, groupId, fund.Id, new { kind = "Adjustment", amountCents = -50_000L, note = "Đếm lại" });
        var removed = await _api.ContributeAsync(b, groupId, fund.Id, 999_000, fromWallet: false);
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, $"{GroupFundApi.Entries(groupId, fund.Id)}/{removed.Entry.Id}", b.Token);

        var summary = await _api.ReadAsync<Summary>(HttpMethod.Get, $"{GroupFundApi.Funds(groupId)}/{fund.Id}/summary?year={thisYear}&month={thisMonth}", viewer.Token);

        summary.OpeningCents.Should().Be(700_000, "1.000.000 góp − 300.000 chi của tháng trước");
        summary.ContributedCents.Should().Be(700_000, "dòng đã xoá không tính");
        summary.ContributionsByMember.Should().BeEquivalentTo(new[] { new MemberAmount(a.Id, "A", 500_000), new MemberAmount(b.Id, "B", 200_000) });
        summary.SpentCents.Should().Be(400_000);
        summary.ExpensesByCategory.Should().ContainSingle(c => c.CategoryId == bills && c.AmountCents == 400_000);
        summary.WithdrawnCents.Should().Be(100_000);
        summary.WithdrawalsByMember.Should().ContainSingle(m => m.MemberUserId == a.Id);
        summary.AdjustmentsCents.Should().Be(-50_000);
        summary.ClosingCents.Should().Be(700_000 + 700_000 - 400_000 - 100_000 - 50_000);
        (await _api.GetFundAsync(a, groupId, fund.Id)).BalanceCents.Should().Be(summary.ClosingCents, "không có dòng nào sau tháng này");

        var previous = VietnamTime.YearMonthOf(lastMonth);
        var before = await _api.ReadAsync<Summary>(HttpMethod.Get, $"{GroupFundApi.Funds(groupId)}/{fund.Id}/summary?year={previous.Year}&month={previous.Month}", a.Token);
        before.OpeningCents.Should().Be(0);
        before.ClosingCents.Should().Be(summary.OpeningCents);
    }

    [Fact]
    public async Task TheFundSummary_RejectsAnImpossibleMonth_AndHidesFromStrangers()
    {
        var (a, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(a, groupId);
        var stranger = await _api.NewUserAsync();

        await _api.ExpectAsync(HttpMethod.Get, $"{GroupFundApi.Funds(groupId)}/{fund.Id}/summary?year=2026&month=13", a.Token, HttpStatusCode.BadRequest);
        await _api.ExpectAsync(HttpMethod.Get, $"{GroupFundApi.Funds(groupId)}/{fund.Id}/summary", stranger.Token, HttpStatusCode.NotFound);
    }
}
