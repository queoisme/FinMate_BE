using System.Net;
using FinMate.Application.Groups.Funds;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, sửa và xoá dòng quỹ: BR-FUND-05 (ai sửa gì), 09 nửa đầu (chủ dòng xoá thì xoá cả giao dịch cá nhân), S3 (quản lý xoá dòng người khác thì
/// giao dịch cá nhân giữ nguyên, chủ dòng được báo), D5, D16. Bất biến: số dư quỹ = Σ dòng còn sống; số dư ví = ban đầu + Σ vế giao dịch gắn quỹ.
/// </summary>
[Collection("Integration")]
public class GroupFundEntryChangeTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundEntryChangeTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record BudgetData(Guid Id, long SpentCents);

    private static string Entry(Guid groupId, Guid fundId, Guid entryId) => $"{GroupFundApi.Entries(groupId, fundId)}/{entryId}";

    private async Task<(GroupFundApi.User Owner, Guid GroupId, Guid FundId)> FundAsync()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        return (owner, groupId, fund.Id);
    }

    private Task<GroupFundApi.EntryResult> PatchAsync(GroupFundApi.User user, Guid groupId, Guid fundId, Guid entryId, object body)
        => _api.ReadAsync<GroupFundApi.EntryResult>(HttpMethod.Patch, Entry(groupId, fundId, entryId), user.Token, body);

    private async Task<Guid> SecondWalletAsync(GroupFundApi.User user)
        => (await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/financial-accounts", user.Token,
            new { accountType = "Cash", accountName = "Tiền mặt", initialBalanceCents = GroupFundApi.InitialBalance })).Id;

    /// <summary>Bất biến của cả phase, đọc thẳng DB.</summary>
    private async Task AssertInvariantsAsync(Guid fundId, params (Guid AccountId, long Initial)[] wallets)
    {
        var (balance, entries) = await _api.WithDbAsync(async db =>
        {
            var fund = await db.GroupFunds.AsNoTracking().SingleAsync(f => f.Id == fundId);
            var live = await db.GroupFundEntries.AsNoTracking().Where(e => e.FundId == fundId).ToListAsync();
            return (fund.BalanceCents, live);
        });
        balance.Should().Be(entries.Sum(GroupFundMath.Signed), "số dư quỹ = Σ dòng còn sống, có dấu");

        foreach (var (accountId, initial) in wallets)
        {
            var legs = await _api.WithDbAsync(db => db.Transactions.AsNoTracking()
                .Where(t => t.FinancialAccountId == accountId && t.GroupFundEntryId != null)
                .Select(t => t.GroupFundDirection == GroupFundDirection.In ? t.AmountCents : -t.AmountCents)
                .ToListAsync());
            (await _api.BalanceAsync(accountId)).Should().Be(initial + legs.Sum(), "số dư ví = ban đầu + Σ vế của giao dịch gắn quỹ còn sống");
        }
    }

    [Fact]
    public async Task OwnerChangesTheAmount_FundAndWalletFollow()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var entry = (await _api.ContributeAsync(owner, groupId, fundId, 500_000)).Entry;

        var result = await PatchAsync(owner, groupId, fundId, entry.Id, new { amountCents = 700_000L, note = "Góp thêm" });

        result.Entry.AmountCents.Should().Be(700_000);
        result.Fund.BalanceCents.Should().Be(700_000);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 700_000);
        (await _api.PersonalLinkAsync(entry.Id))!.Description.Should().Be("Góp thêm");
        await AssertInvariantsAsync(fundId, (owner.AccountId, GroupFundApi.InitialBalance));
    }

    [Fact]
    public async Task OwnerMovesTheEntryToAnotherWallet_ThenRemovesIt_ThenAddsItBack_OnTheSamePersonalRow()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var cash = await SecondWalletAsync(owner);
        var entry = (await _api.ContributeAsync(owner, groupId, fundId, 500_000)).Entry;
        var linkId = (await _api.PersonalLinkAsync(entry.Id))!.Id;
        var wallets = new[] { (owner.AccountId, GroupFundApi.InitialBalance), (cash, GroupFundApi.InitialBalance) };

        await PatchAsync(owner, groupId, fundId, entry.Id, new { financialAccountId = cash });
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance);
        (await _api.BalanceAsync(cash)).Should().Be(GroupFundApi.InitialBalance - 500_000);
        await AssertInvariantsAsync(fundId, wallets);

        var removed = await PatchAsync(owner, groupId, fundId, entry.Id, new { removeFinancialAccount = true });
        removed.Entry.HasPersonalLink.Should().BeFalse();
        (await _api.BalanceAsync(cash)).Should().Be(GroupFundApi.InitialBalance);
        removed.Fund.BalanceCents.Should().Be(500_000, "bỏ ví không đổi quỹ");
        await AssertInvariantsAsync(fundId, wallets);

        await PatchAsync(owner, groupId, fundId, entry.Id, new { financialAccountId = owner.AccountId, amountCents = 400_000L });
        (await _api.PersonalLinkAsync(entry.Id))!.Id.Should().Be(linkId, "mỗi dòng quỹ chỉ có một giao dịch cá nhân");
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 400_000);
        await AssertInvariantsAsync(fundId, wallets);
    }

    [Fact]
    public async Task AddingAWalletLater_AndEditingAWithdrawal_KeepTheDirection()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var contribution = (await _api.ContributeAsync(owner, groupId, fundId, 1_000_000, fromWallet: false)).Entry;
        var withdrawal = (await _api.RecordAsync(owner, groupId, fundId, new { kind = "Withdrawal", amountCents = 300_000L, financialAccountId = owner.AccountId })).Entry;

        await PatchAsync(owner, groupId, fundId, contribution.Id, new { financialAccountId = owner.AccountId });
        await PatchAsync(owner, groupId, fundId, withdrawal.Id, new { amountCents = 200_000L });

        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance - 1_000_000 + 200_000);
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(800_000);
        await AssertInvariantsAsync(fundId, (owner.AccountId, GroupFundApi.InitialBalance));
    }

    [Fact]
    public async Task NobodyChangesTheAmountOfSomeoneElsesEntry_ButAManagerMayFixItsNoteAndCategory()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var manager = await _api.JoinAsync(owner, groupId, "Manager");
        var member = await _api.JoinAsync(owner, groupId);
        var other = await _api.JoinAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var food = await _api.SystemCategoryAsync(owner.Token, "food");
        var expense = (await _api.SpendAsync(member, groupId, fundId, 800_000, bills)).Entry;
        var url = Entry(groupId, fundId, expense.Id);

        await _api.ExpectAsync(HttpMethod.Patch, url, other.Token, HttpStatusCode.Forbidden, new { amountCents = 1L });
        await _api.ExpectAsync(HttpMethod.Patch, url, other.Token, HttpStatusCode.Forbidden, new { note = "sửa" });
        await _api.ExpectAsync(HttpMethod.Patch, url, manager.Token, HttpStatusCode.Forbidden, new { amountCents = 1L });
        await _api.ExpectAsync(HttpMethod.Patch, url, owner.Token, HttpStatusCode.Forbidden, new { transactedAt = DateTimeOffset.UtcNow.AddDays(-1) });

        var fixedUp = await PatchAsync(manager, groupId, fundId, expense.Id, new { categoryId = food, note = "Tiền chợ" });
        fixedUp.Entry.CategoryId.Should().Be(food);
        fixedUp.Entry.AmountCents.Should().Be(800_000);
        fixedUp.Entry.MemberUserId.Should().Be(member.Id, "người ghi không đổi");
    }

    [Fact]
    public async Task EditingAnExpense_MovesItBetweenGroupBudgets()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var food = await _api.SystemCategoryAsync(owner.Token, "food");
        var billsBudget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });
        var foodBudget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = food, limitCents = 1_000_000L });
        var expense = (await _api.SpendAsync(owner, groupId, fundId, 800_000, bills)).Entry;

        await PatchAsync(owner, groupId, fundId, expense.Id, new { categoryId = food, amountCents = 600_000L });

        var budgets = await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token);
        budgets.Single(b => b.Id == billsBudget.Id).SpentCents.Should().Be(0);
        budgets.Single(b => b.Id == foodBudget.Id).SpentCents.Should().Be(600_000);

        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, Entry(groupId, fundId, expense.Id), owner.Token);
        budgets = await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token);
        budgets.Single(b => b.Id == foodBudget.Id).SpentCents.Should().Be(0, "xoá khoản chi từ quỹ trả lại ngân sách nhóm");
    }

    [Fact]
    public async Task TheOwnerDeletingTheirEntry_AlsoRemovesTheirPersonalTransaction_AndRefundsTheWallet()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var entry = (await _api.ContributeAsync(owner, groupId, fundId, 500_000)).Entry;
        var linkId = (await _api.PersonalLinkAsync(entry.Id))!.Id;

        var fund = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, Entry(groupId, fundId, entry.Id), owner.Token);

        fund.BalanceCents.Should().Be(0);
        (await _api.BalanceAsync(owner.AccountId)).Should().Be(GroupFundApi.InitialBalance, "BR-FUND-09: hoàn số dư trong cùng lượt lưu");
        var deletedAt = await _api.WithDbAsync(db => db.Transactions.IgnoreQueryFilters().Where(t => t.Id == linkId).Select(t => t.DeletedAt).SingleAsync());
        deletedAt.Should().NotBeNull();
        await _api.ExpectAsync(HttpMethod.Delete, Entry(groupId, fundId, entry.Id), owner.Token, HttpStatusCode.NotFound);
        await AssertInvariantsAsync(fundId, (owner.AccountId, GroupFundApi.InitialBalance));
    }

    [Fact]
    public async Task AManagerDeletingSomeoneElsesEntry_LeavesTheirPersonalTransaction_AndTellsThem()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var entry = (await _api.ContributeAsync(member, groupId, fundId, 500_000)).Entry;

        var fund = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, Entry(groupId, fundId, entry.Id), owner.Token);

        fund.BalanceCents.Should().Be(0, "dòng quỹ bị xoá mềm, số dư tính lại");
        var link = await _api.PersonalLinkAsync(entry.Id);
        link.Should().NotBeNull("S3: giao dịch cá nhân của chủ dòng giữ nguyên");
        link!.GroupFundEntryId.Should().Be(entry.Id);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000, "không ai đụng vào ví của người khác");

        var (activity, audit, alert) = await _api.WithDbAsync(async db => (
            await db.GroupActivities.AnyAsync(a => a.GroupId == groupId && a.Type == GroupActivityType.FundEntryDeleted && a.SubjectId == entry.Id),
            await db.AuditLogs.AnyAsync(a => a.EventType == "Group.Fund.Entry.Deleted" && a.UserId == owner.Id),
            await db.ForecastAlerts.AnyAsync(a => a.UserId == member.Id && a.Kind == ForecastAlertKind.GroupFundRemoved)));
        activity.Should().BeTrue();
        audit.Should().BeTrue();
        alert.Should().BeTrue("chủ dòng được báo, không số tiền");
        await AssertInvariantsAsync(fundId);
    }

    [Fact]
    public async Task AMemberCannotDeleteSomeoneElsesEntry()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var entry = (await _api.ContributeAsync(owner, groupId, fundId, 500_000, fromWallet: false)).Entry;

        await _api.ExpectAsync(HttpMethod.Delete, Entry(groupId, fundId, entry.Id), member.Token, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManyChanges_KeepEveryBalanceConsistent()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var cash = await SecondWalletAsync(owner);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var wallets = new[] { (owner.AccountId, GroupFundApi.InitialBalance), (cash, GroupFundApi.InitialBalance), (member.AccountId, GroupFundApi.InitialBalance) };

        var a = (await _api.ContributeAsync(owner, groupId, fundId, 500_000)).Entry;
        var b = (await _api.ContributeAsync(member, groupId, fundId, 500_000)).Entry;
        var c = (await _api.SpendAsync(member, groupId, fundId, 800_000, bills, accountId: member.AccountId)).Entry;
        var d = (await _api.RecordAsync(owner, groupId, fundId, new { kind = "Withdrawal", amountCents = 100_000L, financialAccountId = cash })).Entry;
        await _api.RecordAsync(owner, groupId, fundId, new { kind = "Adjustment", amountCents = 20_000L, note = "Đếm lại" });
        await AssertInvariantsAsync(fundId, wallets);

        await PatchAsync(owner, groupId, fundId, a.Id, new { financialAccountId = cash, amountCents = 450_000L });
        await PatchAsync(member, groupId, fundId, c.Id, new { amountCents = 900_000L });
        await PatchAsync(owner, groupId, fundId, d.Id, new { removeFinancialAccount = true });
        await AssertInvariantsAsync(fundId, wallets);

        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, Entry(groupId, fundId, b.Id), member.Token);
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, Entry(groupId, fundId, c.Id), owner.Token);   // quản lý xoá: ví của member giữ nguyên
        await AssertInvariantsAsync(fundId, (owner.AccountId, GroupFundApi.InitialBalance), (cash, GroupFundApi.InitialBalance));
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance - 900_000, "khoản chi của member vẫn ở sổ cá nhân của họ (S3)");
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(450_000 - 100_000 + 20_000);
    }

    [Fact]
    public async Task AnArchivedFund_CannotBeEdited()
    {
        var (owner, groupId, fundId) = await FundAsync();
        var entry = (await _api.ContributeAsync(owner, groupId, fundId, 100_000, fromWallet: false)).Entry;
        await _api.RecordAsync(owner, groupId, fundId, new { kind = "Withdrawal", amountCents = 100_000L });
        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, $"{GroupFundApi.Funds(groupId)}/{fundId}/archive", owner.Token);

        await _api.ExpectAsync(HttpMethod.Patch, Entry(groupId, fundId, entry.Id), owner.Token, HttpStatusCode.UnprocessableEntity, new { note = "x" }, "GROUP_FUND_ARCHIVED");
        await _api.ExpectAsync(HttpMethod.Delete, Entry(groupId, fundId, entry.Id), owner.Token, HttpStatusCode.UnprocessableEntity, code: "GROUP_FUND_ARCHIVED");
    }
}
