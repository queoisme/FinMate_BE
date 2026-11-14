using System.Net;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, từ phía sổ cá nhân: BR-FUND-09 nửa sau (xoá giao dịch gắn quỹ thì dòng quỹ đi theo), D7 (đã rời nhóm thì dòng ở lại), D8 (cờ tắt vẫn
/// đồng bộ), D9 (không sửa, chuyển ví, hoàn, chia sẻ qua đường giao dịch), D11 (thông báo ngân hàng gộp vào giao dịch gắn quỹ, giao dịch đó thắng).
/// </summary>
[Collection("Integration")]
public class GroupFundPersonalSyncTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundPersonalSyncTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    private async Task<(GroupFundApi.User Owner, GroupFundApi.User Member, Guid GroupId, Guid FundId)> SetupAsync()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var member = await _api.JoinAsync(owner, groupId);
        var fund = await _api.CreateFundAsync(owner, groupId);
        return (owner, member, groupId, fund.Id);
    }

    private async Task<(Guid EntryId, Guid TransactionId)> ContributeAsync(GroupFundApi.User user, Guid groupId, Guid fundId, long amount = 500_000)
    {
        var entry = (await _api.ContributeAsync(user, groupId, fundId, amount)).Entry;
        return (entry.Id, (await _api.PersonalLinkAsync(entry.Id))!.Id);
    }

    private Task<GroupFundEntry> EntryAsync(Guid entryId) => _api.WithDbAsync(db => db.GroupFundEntries.AsNoTracking().SingleAsync(e => e.Id == entryId));

    [Fact]
    public async Task DeletingThePersonalTransaction_DeletesTheFundLine_AndRefundsTheWallet()
    {
        var (owner, member, groupId, fundId) = await SetupAsync();
        var (entryId, txId) = await ContributeAsync(member, groupId, fundId);

        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{txId}", member.Token, HttpStatusCode.NoContent);

        (await EntryAsync(entryId)).DeletedAt.Should().NotBeNull("BR-FUND-09");
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(0);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance);
    }

    [Fact]
    public async Task DeletingAConvertedBankDebit_DeletesTheFundExpense_AndTheGroupBudgetFollows()
    {
        var (owner, member, groupId, fundId) = await SetupAsync();
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var budget = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });
        var debit = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", member.Token, new
        {
            financialAccountId = member.AccountId, categoryId = bills, amountCents = 800_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, merchantName = "EVN",
        });
        var entry = (await _api.RecordAsync(member, groupId, fundId, new { kind = "Expense", fromTransactionId = debit.Id })).Entry;

        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{debit.Id}", member.Token, HttpStatusCode.NoContent);

        (await EntryAsync(entry.Id)).DeletedAt.Should().NotBeNull();
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(0);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance);
        var spent = await _api.WithDbAsync(db => db.BudgetPeriods.AsNoTracking().Where(p => p.BudgetId == budget.Id).Select(p => p.SpentCents).SingleAsync());
        spent.Should().Be(0);
    }

    [Fact]
    public async Task AfterLeavingTheGroup_DeletingThePersonalTransaction_LeavesTheFundLine()
    {
        var (owner, member, groupId, fundId) = await SetupAsync();
        var (entryId, txId) = await ContributeAsync(member, groupId, fundId);
        (await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", member.Token)).EnsureSuccessStatusCode();

        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{txId}", member.Token, HttpStatusCode.NoContent);

        (await EntryAsync(entryId)).DeletedAt.Should().BeNull("D7: người đã rời không đổi được sổ quỹ");
        (await _api.GetFundAsync(owner, groupId, fundId)).BalanceCents.Should().Be(500_000);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance, "sổ cá nhân của họ vẫn xoá được, ví vẫn được hoàn");
    }

    [Fact]
    public async Task WithTheFundFlagOff_DeletingThePersonalTransaction_StillSyncsTheFund()
    {
        var (owner, member, groupId, fundId) = await SetupAsync();
        var (entryId, txId) = await ContributeAsync(member, groupId, fundId);
        await _factory.SetFeaturesAsync(groupFunds: false);

        await _api.ExpectAsync(HttpMethod.Get, GroupFundApi.Funds(groupId), owner.Token, HttpStatusCode.UnprocessableEntity, code: "GROUP_FUNDS_DISABLED");
        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{txId}", member.Token, HttpStatusCode.NoContent);

        (await EntryAsync(entryId)).DeletedAt.Should().NotBeNull("D8: tắt cờ không được làm số của nhóm lệch");
        var balance = await _api.WithDbAsync(db => db.GroupFunds.AsNoTracking().Where(f => f.Id == fundId).Select(f => f.BalanceCents).SingleAsync());
        balance.Should().Be(0);
    }

    [Fact]
    public async Task APersonalFundTransaction_IsOnlyChangedThroughItsFundLine()
    {
        var (owner, member, groupId, fundId) = await SetupAsync();
        var (_, txId) = await ContributeAsync(member, groupId, fundId);
        var cash = (await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/financial-accounts", member.Token,
            new { accountType = "Cash", accountName = "Tiền mặt", initialBalanceCents = 0L })).Id;
        var tx = $"/api/v1/transactions/{txId}";
        const string Linked = "TRANSACTION_GROUP_FUND_LINKED";

        await _api.ExpectAsync(HttpMethod.Patch, tx, member.Token, HttpStatusCode.UnprocessableEntity, new
        {
            financialAccountId = member.AccountId, counterAccountId = cash, amountCents = 1L, transactionType = "Transfer", transactedAt = DateTimeOffset.UtcNow,
        }, Linked);
        await _api.ExpectAsync(HttpMethod.Post, $"{tx}/convert-to-transfer", member.Token, HttpStatusCode.UnprocessableEntity, new { counterAccountId = cash }, Linked);
        await _api.ExpectAsync(HttpMethod.Put, $"{tx}/group", member.Token, HttpStatusCode.UnprocessableEntity, new { groupId }, Linked);
        await _api.ExpectAsync(HttpMethod.Post, $"{tx}/refunds", member.Token, HttpStatusCode.UnprocessableEntity, new { amountCents = 1L }, Linked);
        await _api.ExpectAsync(HttpMethod.Put, $"{tx}/related", member.Token, HttpStatusCode.UnprocessableEntity, new { relatedTransactionId = (Guid?)null, relation = "Refund" }, Linked);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000, "không có gì đổi");
    }

    [Fact]
    public async Task ABankNoticeOfTheContribution_MergesIntoTheFundTransaction_WhichKeepsItsLink()
    {
        var (_, member, groupId, fundId) = await SetupAsync();
        var (entryId, txId) = await ContributeAsync(member, groupId, fundId);
        var notice = await _api.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = member.Id, FinancialAccountId = member.AccountId, AmountCents = 500_000, TransactionType = TransactionType.Debit,
                Source = TransactionSource.Notification, Status = TransactionStatus.Draft, MerchantName = "CK quy phong", TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });

        var merged = await _api.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{notice}/merge", member.Token, new { withTransactionId = txId });
        merged.IsSuccessStatusCode.Should().BeTrue(await merged.Content.ReadAsStringAsync());

        var (survivor, loser) = await _api.WithDbAsync(async db => (
            await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId),
            await db.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == notice)));
        survivor.GroupFundEntryId.Should().Be(entryId, "D11: giao dịch gắn quỹ thắng và giữ liên kết");
        survivor.MergedSources.Should().Contain("notification");
        loser.MergedIntoTransactionId.Should().Be(txId);
        (await _api.BalanceAsync(member.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000, "nháp chưa đổi số dư, gộp không đổi thêm");
    }
}
