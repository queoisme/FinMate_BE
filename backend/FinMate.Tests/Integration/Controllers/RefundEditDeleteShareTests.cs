using System.Net;
using System.Net.Http.Json;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 81: sửa và xoá khoản gốc / khoản hoàn (BR-RF-11, D13, D18), và khoản hoàn đi theo khoản gốc vào nhóm (BR-RF-10, R4).</summary>
[Collection("Integration")]
public class RefundEditDeleteShareTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public RefundEditDeleteShareTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> PatchAsync(RefundApi.Session s, RefundApi.TransactionData t, long? amount = null, Guid? category = null, string? type = null, DateTimeOffset? at = null)
        => _api.SendAsync(HttpMethod.Patch, $"/api/v1/transactions/{t.Id}", s.Token, new
        {
            financialAccountId = t.FinancialAccountId,
            categoryId = category ?? t.CategoryId,
            amountCents = amount ?? t.AmountCents,
            transactionType = type ?? t.TransactionType,
            transactedAt = at ?? t.TransactedAt,
            merchantName = "Shopee - Giày",
        });

    private Task<HttpResponseMessage> DeleteAsync(RefundApi.Session s, Guid id)
        => _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{id}", s.Token);

    [Fact]
    public async Task DeletingOrResizingARefundedOriginal_Is409()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 });

        var delete = await DeleteAsync(s, shoes.Id);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RefundApi.ErrorAsync(delete)).Should().Be("HAS_REFUNDS");

        var resize = await PatchAsync(s, shoes, amount: 800_000);
        resize.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RefundApi.ErrorAsync(resize)).Should().Be("HAS_REFUNDS");
    }

    [Fact]
    public async Task RecategorisingTheOriginal_TakesItsRefundsAlong()
    {
        var s = await _api.StartAsync();
        var shopping = await _api.BudgetAsync(s, 1_000_000);
        var food = await _api.BudgetAsync(s, 1_000_000, s.Food);
        var shoes = await _api.SpendAsync(s, 900_000);
        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 }));

        (await PatchAsync(s, shoes, category: s.Food)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.GetAsync(s, refund.Id)).CategoryId.Should().Be(s.Food);
        (await _api.PeriodAsync(shopping)).SpentCents.Should().Be(0);
        (await _api.PeriodAsync(food)).SpentCents.Should().Be(600_000);
    }

    [Fact]
    public async Task EditingALinkedRefund_CategoryIsLocked_TypeIsFixed_AmountIsCapped()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 500_000 });
        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 }));

        (await RefundApi.ErrorAsync(await PatchAsync(s, refund, category: s.Food))).Should().Be("REFUND_CATEGORY_LOCKED");
        (await RefundApi.ErrorAsync(await PatchAsync(s, refund, type: "Debit"))).Should().Be("TRANSACTION_TYPE_CHANGE_NOT_ALLOWED");
        (await RefundApi.ErrorAsync(await PatchAsync(s, refund, amount: 400_001))).Should().Be("REFUND_EXCEEDS_ORIGINAL");

        var balance = await _api.BalanceAsync(s.Account);
        (await PatchAsync(s, refund, amount: 400_000)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.BalanceAsync(s.Account)).Should().Be(balance + 100_000);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task DeletingARefund_UndoesItsBalanceAndBudgetEffect()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        var balanceBefore = await _api.BalanceAsync(s.Account);
        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 }));

        (await DeleteAsync(s, refund.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _api.BalanceAsync(s.Account)).Should().Be(balanceBefore);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(900_000);
        (await DeleteAsync(s, shoes.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent, "hết khoản hoàn thì khoản gốc xoá được");
    }

    [Fact]
    public async Task CrossMonthRefund_ThenDeletingIt_DoesNotInventSpending()
    {
        // D2 đầu-cuối: mua tháng trước, hoàn tháng này (tháng này chưa chi gì) rồi xoá khoản hoàn: kỳ tháng này vẫn 0.
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1);
        var shoes = await _api.SpendAsync(s, 900_000, lastMonth);
        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id));

        (await _api.PeriodAsync(budget, DateTimeOffset.UtcNow)).SpentCents.Should().Be(0);
        await DeleteAsync(s, refund.Id);
        (await _api.PeriodAsync(budget, DateTimeOffset.UtcNow)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task DeletingAReversal_PutsTheOriginalBack()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var wrong = await _api.SpendAsync(s, 500_000);
        var reversal = await RefundApi.DataAsync(await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal" }));

        await DeleteAsync(s, reversal.Id);

        (await _api.GetAsync(s, wrong.Id)).IsReversed.Should().BeFalse();
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(500_000);
    }

    [Fact]
    public async Task DeletingAnOriginal_ReleasesDraftsPointingAtIt()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var draft = await _api.DraftCreditAsync(s, 900_000);
        await _api.LinkAsync(s, draft, shoes.Id);

        (await DeleteAsync(s, shoes.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var released = await _api.GetAsync(s, draft);
        released.RelatedTransactionId.Should().BeNull();
        released.Relation.Should().BeNull();
    }

    private record GroupBudget(Guid Id, long SpentCents);

    private async Task<(Guid Group, Guid Budget)> GroupWithBudgetAsync(RefundApi.Session s)
    {
        var group = await _api.SendAsync(HttpMethod.Post, "/api/v1/groups", s.Token, new { name = "Gia đình" });
        var groupId = (await group.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        var budget = await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", s.Token, new { categoryId = s.Shopping, limitCents = 5_000_000L });
        var budgetId = (await budget.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        return (groupId, budgetId);
    }

    private async Task<long> GroupSpentAsync(RefundApi.Session s, Guid group, Guid budget)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/v1/groups/{group}/budgets", s.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<List<GroupBudget>>>(RefundApi.JsonOptions))!.Data.Single(b => b.Id == budget).SpentCents;
    }

    [Fact]
    public async Task RefundOfASharedPurchase_FollowsItIntoTheGroup()
    {
        var s = await _api.StartAsync();
        var (group, budget) = await GroupWithBudgetAsync(s);
        var shoes = await _api.SpendAsync(s, 900_000);
        (await _api.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{shoes.Id}/group", s.Token, new { groupId = group })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GroupSpentAsync(s, group, budget)).Should().Be(900_000);

        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000 }));

        refund.GroupId.Should().Be(group);
        (await GroupSpentAsync(s, group, budget)).Should().Be(600_000);

        var alone = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{refund.Id}/group", s.Token);
        alone.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(alone)).Should().Be("REFUND_FOLLOWS_ORIGINAL");

        (await _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{shoes.Id}/group", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.GetAsync(s, refund.Id)).GroupId.Should().BeNull();
        (await GroupSpentAsync(s, group, budget)).Should().Be(0);
    }

    [Fact]
    public async Task ReversingASharedPurchase_RecomputesTheGroupPeriodOfTheOriginal()
    {
        // R4: khoản gốc ở tháng trước, lệnh đảo tháng này: kỳ NHÓM của tháng trước được tính lại trong cùng lượt lưu.
        var s = await _api.StartAsync();
        var (group, budget) = await GroupWithBudgetAsync(s);
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1);
        var wrong = await _api.SpendAsync(s, 500_000, lastMonth);
        await _api.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{wrong.Id}/group", s.Token, new { groupId = group });
        (await _api.PeriodAsync(budget, lastMonth)).SpentCents.Should().Be(500_000);

        await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal" });

        (await _api.PeriodAsync(budget, lastMonth)).SpentCents.Should().Be(0);
    }
}
