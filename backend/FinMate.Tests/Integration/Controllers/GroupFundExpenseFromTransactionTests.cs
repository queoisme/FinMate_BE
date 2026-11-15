using System.Net;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, S4a, D23, D24: khoản chi từ quỹ lấy từ một khoản chi có sẵn của người ghi. Giao dịch thành Transfer gắn quỹ chiều ra: ví không đổi,
/// chi tiêu cá nhân như thể chưa có khoản đó, ngân sách nhóm nhận khoản chi. Xoá dòng thì khoản chi cá nhân trở lại với danh mục gốc.
/// </summary>
[Collection("Integration")]
public class GroupFundExpenseFromTransactionTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const long Electricity = 800_000;

    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundExpenseFromTransactionTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record IdData(Guid Id);
    private sealed record BudgetData(Guid Id, long SpentCents);
    private sealed record TxData(Guid Id, string TransactionType, Guid? CategoryId);

    private sealed record Setup(GroupFundApi.User Owner, GroupFundApi.User Keeper, Guid GroupId, Guid FundId, Guid Bills, Guid GroupBudget);

    private async Task<Setup> SetupAsync()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var keeper = await _api.JoinAsync(owner, groupId, name: "B");
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var budget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });
        return new Setup(owner, keeper, groupId, fund.Id, bills, budget.Id);
    }

    /// <summary>Thông báo VCB −800k "EVN" thành nháp như đường thông báo, rồi người dùng xác nhận.</summary>
    private async Task<Guid> BankDebitAsync(GroupFundApi.User user, Guid categoryId, long amount = Electricity)
    {
        var draftId = await _api.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = user.Id, FinancialAccountId = user.AccountId, CategoryId = categoryId, AmountCents = amount,
                TransactionType = TransactionType.Debit, Source = TransactionSource.Notification, Status = TransactionStatus.Draft,
                MerchantName = "EVN", Description = "Tiền điện tháng 11", TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });
        await _api.ReadAsync<object>(HttpMethod.Post, $"/api/v1/transactions/{draftId}/confirm", user.Token, new { categoryId });
        return draftId;
    }

    private Task<GroupFundApi.EntryResult> FromTransactionAsync(Setup s, Guid transactionId, object? extra = null)
        => _api.RecordAsync(s.Keeper, s.GroupId, s.FundId, extra ?? new { kind = "Expense", fromTransactionId = transactionId });

    private async Task<long> GroupBudgetSpentAsync(Setup s)
        => (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{s.GroupId}/budgets", s.Owner.Token)).Single(b => b.Id == s.GroupBudget).SpentCents;

    [Fact]
    public async Task ScenarioI_TheKeeperTurnsABankDebitIntoAFundExpense()
    {
        var s = await SetupAsync();
        var spendingBefore = await _api.MonthlySummaryAsync(s.Keeper);
        var txId = await BankDebitAsync(s.Keeper, s.Bills);
        (await _api.MonthlySummaryAsync(s.Keeper)).TotalSpentCents.Should().Be(spendingBefore.TotalSpentCents + Electricity);

        var result = await FromTransactionAsync(s, txId);

        result.Fund.BalanceCents.Should().Be(-Electricity, "quỹ −800k");
        result.Entry.AmountCents.Should().Be(Electricity);
        result.Entry.Origin.Should().Be("Converted");
        result.Entry.CategoryId.Should().Be(s.Bills, "lấy danh mục hệ thống của giao dịch");
        result.Entry.MerchantName.Should().Be("EVN");
        (await GroupBudgetSpentAsync(s)).Should().Be(Electricity, "ngân sách nhóm +800k");
        (await _api.MonthlySummaryAsync(s.Keeper)).Should().Be(spendingBefore, "chi tiêu cá nhân của B như trước khi có khoản này");
        (await _api.BalanceAsync(s.Keeper.AccountId)).Should().Be(GroupFundApi.InitialBalance - Electricity, "VCB vẫn sau −800k");

        var tx = await _api.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId));
        tx.TransactionType.Should().Be(TransactionType.Transfer);
        tx.GroupFundEntryId.Should().Be(result.Entry.Id);
        tx.CategoryId.Should().BeNull();
        var snapshot = await _api.WithDbAsync(db => db.TransactionMergeSnapshots.AnyAsync(x => x.SurvivorId == txId && x.Kind == MergeSnapshotKind.GroupFundExpense));
        snapshot.Should().BeTrue("ghi transaction_merge_snapshots như Phase 83");
    }

    [Fact]
    public async Task ThePersonalBudget_IsRecomputed_BothWays()
    {
        var s = await SetupAsync();
        var personalBudget = await _api.ReadAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", s.Keeper.Token, new { categoryId = s.Bills, limitCents = 2_000_000L });
        var txId = await BankDebitAsync(s.Keeper, s.Bills);
        (await PersonalSpentAsync(personalBudget.Id)).Should().Be(Electricity);

        var entry = (await FromTransactionAsync(s, txId)).Entry;
        (await PersonalSpentAsync(personalBudget.Id)).Should().Be(0);

        // D24: chủ dòng xoá → khoản chi trở lại với danh mục gốc, quỹ và ngân sách nhóm trả lại, ví không đổi.
        var fund = await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, $"{GroupFundApi.Entries(s.GroupId, s.FundId)}/{entry.Id}", s.Keeper.Token);
        fund.BalanceCents.Should().Be(0);
        (await PersonalSpentAsync(personalBudget.Id)).Should().Be(Electricity);
        (await GroupBudgetSpentAsync(s)).Should().Be(0);
        (await _api.BalanceAsync(s.Keeper.AccountId)).Should().Be(GroupFundApi.InitialBalance - Electricity);
        var tx = await _api.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId));
        tx.TransactionType.Should().Be(TransactionType.Debit);
        tx.CategoryId.Should().Be(s.Bills);
        tx.GroupFundEntryId.Should().BeNull();
    }

    [Fact]
    public async Task AfterAManagerDeletesTheLine_TheOwnerRestoresTheirSpendingThemselves()
    {
        var s = await SetupAsync();
        var txId = await BankDebitAsync(s.Keeper, s.Bills);
        var entry = (await FromTransactionAsync(s, txId)).Entry;
        var restoreUrl = $"/api/v1/transactions/{txId}/restore-from-fund";
        await _api.ExpectAsync(HttpMethod.Post, restoreUrl, s.Keeper.Token, HttpStatusCode.UnprocessableEntity, code: "TRANSACTION_GROUP_FUND_LINKED");

        await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Delete, $"{GroupFundApi.Entries(s.GroupId, s.FundId)}/{entry.Id}", s.Owner.Token);
        (await _api.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId))).TransactionType.Should().Be(TransactionType.Transfer, "S3");

        await _api.ExpectAsync(HttpMethod.Post, restoreUrl, s.Owner.Token, HttpStatusCode.NotFound);
        var restored = await _api.ReadAsync<TxData>(HttpMethod.Post, restoreUrl, s.Keeper.Token);
        restored.TransactionType.Should().Be("Debit");
        restored.CategoryId.Should().Be(s.Bills);
        (await _api.BalanceAsync(s.Keeper.AccountId)).Should().Be(GroupFundApi.InitialBalance - Electricity);
    }

    [Fact]
    public async Task AConvertedLine_KeepsTheBankFacts()
    {
        var s = await SetupAsync();
        var food = await _api.SystemCategoryAsync(s.Owner.Token, "food");
        var txId = await BankDebitAsync(s.Keeper, s.Bills);
        var entry = (await FromTransactionAsync(s, txId)).Entry;
        var url = $"{GroupFundApi.Entries(s.GroupId, s.FundId)}/{entry.Id}";

        await _api.ExpectAsync(HttpMethod.Patch, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity, new { amountCents = 1L }, "GROUP_FUND_ENTRY_LOCKED");
        await _api.ExpectAsync(HttpMethod.Patch, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity, new { removeFinancialAccount = true }, "GROUP_FUND_ENTRY_LOCKED");

        var edited = await _api.ReadAsync<GroupFundApi.EntryResult>(HttpMethod.Patch, url, s.Keeper.Token, new { categoryId = food, note = "Điện + nước" });
        edited.Entry.CategoryId.Should().Be(food);
        var tx = await _api.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId));
        tx.Description.Should().Be("Tiền điện tháng 11", "mô tả của giao dịch ngân hàng không bị ghi đè");
    }

    [Fact]
    public async Task OnlyAnEligibleDebit_CanBeConverted()
    {
        var s = await SetupAsync();
        var url = GroupFundApi.Entries(s.GroupId, s.FundId);
        var loan = await _api.SystemCategoryAsync(s.Owner.Token, "loan");
        var mine = await _api.ReadAsync<IdData>(HttpMethod.Post, "/api/v1/categories", s.Keeper.Token, new { name = "Điện nhà tôi", iconName = "bolt" });

        var draft = await _api.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var t = new Transaction
            {
                Id = Guid.NewGuid(), UserId = s.Keeper.Id, FinancialAccountId = s.Keeper.AccountId, CategoryId = s.Bills, AmountCents = 1, TransactionType = TransactionType.Debit,
                Source = TransactionSource.Notification, Status = TransactionStatus.Draft, TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });
        var lent = await BankDebitAsync(s.Keeper, loan, 200_000);
        var shared = await BankDebitAsync(s.Keeper, s.Bills, 100_000);
        (await _api.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{shared}/group", s.Keeper.Token, new { groupId = s.GroupId })).EnsureSuccessStatusCode();
        var refunded = await BankDebitAsync(s.Keeper, s.Bills, 300_000);
        (await _api.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{refunded}/refunds", s.Keeper.Token, new { amountCents = 100_000L })).EnsureSuccessStatusCode();
        var someoneElses = await BankDebitAsync(s.Owner, s.Bills, 50_000);
        var private1 = await BankDebitAsync(s.Keeper, mine.Id, 70_000);
        var ok = await BankDebitAsync(s.Keeper, s.Bills, 90_000);

        foreach (var id in new[] { draft, lent, shared, refunded })
        {
            await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
                new { kind = "Expense", fromTransactionId = id }, "GROUP_FUND_SOURCE_NOT_ELIGIBLE");
        }

        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.NotFound, new { kind = "Expense", fromTransactionId = someoneElses });
        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Expense", fromTransactionId = private1 }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Expense", fromTransactionId = ok, amountCents = 1L }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Contribution", fromTransactionId = ok }, "GROUP_FUND_ENTRY_INVALID");
        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Expense", fromTransactionId = ok, financialAccountId = s.Keeper.AccountId }, "GROUP_FUND_ENTRY_INVALID");

        var withChosenCategory = await FromTransactionAsync(s, private1, new { kind = "Expense", fromTransactionId = private1, categoryId = s.Bills });
        withChosenCategory.Entry.CategoryId.Should().Be(s.Bills, "danh mục riêng của người ghi thì phải chọn danh mục chung");
        (await GroupBudgetSpentAsync(s)).Should().Be(100_000 + 70_000, "khoản đã chia sẻ + khoản vừa chuyển");
        await _api.ExpectAsync(HttpMethod.Post, url, s.Keeper.Token, HttpStatusCode.UnprocessableEntity,
            new { kind = "Expense", fromTransactionId = private1, categoryId = s.Bills }, "GROUP_FUND_SOURCE_NOT_ELIGIBLE");
    }

    private Task<long> PersonalSpentAsync(Guid budgetId)
        => _api.WithDbAsync(db => db.BudgetPeriods.AsNoTracking().Where(p => p.BudgetId == budgetId).OrderByDescending(p => p.PeriodStart).Select(p => p.SpentCents).FirstAsync());
}
