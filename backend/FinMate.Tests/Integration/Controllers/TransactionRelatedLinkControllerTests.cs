using System.Net;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 81: <c>PUT/DELETE /transactions/{id}/related</c>, <c>confirm</c> có <c>relatedTransactionId</c>, hoàn mồ côi qua <c>POST /transactions</c>.</summary>
[Collection("Integration")]
public class TransactionRelatedLinkControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly RefundApi _api;

    public TransactionRelatedLinkControllerTests(AuthApiFactory factory)
    {
        _api = new RefundApi(factory);
    }

    [Fact]
    public async Task LinkingAConfirmedIncome_TurnsItIntoARefund_InOneSave_BalanceUnchanged()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        var income = await _api.EarnAsync(s, 900_000);
        var balance = await _api.BalanceAsync(s.Account);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(900_000);

        var response = await _api.LinkAsync(s, income.Id, shoes.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var linked = await RefundApi.DataAsync(response);
        linked.Relation.Should().Be("Refund");
        linked.RelatedTransactionId.Should().Be(shoes.Id);
        linked.CategoryId.Should().Be(s.Shopping);
        (await _api.BalanceAsync(s.Account)).Should().Be(balance, "liên kết không di chuyển tiền");
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task Unlinking_GoesBackToPlainIncome()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        var income = await _api.EarnAsync(s, 300_000);
        await _api.LinkAsync(s, income.Id, shoes.Id);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(600_000);

        var response = await _api.UnlinkAsync(s, income.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plain = await RefundApi.DataAsync(response);
        plain.Relation.Should().BeNull();
        plain.RelatedTransactionId.Should().BeNull();
        plain.CategoryId.Should().Be(s.Income);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(900_000);
        (await _api.GetAsync(s, shoes.Id)).RefundedCents.Should().Be(0);
    }

    [Fact]
    public async Task UnlinkingAReversal_BringsTheOriginalBack()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var wrong = await _api.SpendAsync(s, 500_000);
        var reversal = await RefundApi.DataAsync(await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal" }));
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(0);

        await _api.UnlinkAsync(s, reversal.Id);

        (await _api.GetAsync(s, wrong.Id)).IsReversed.Should().BeFalse();
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(500_000);
    }

    [Fact]
    public async Task SomeoneElsesTransaction_OnEitherEnd_Is404()
    {
        var owner = await _api.StartAsync();
        var stranger = await _api.StartAsync();
        var ownersShoes = await _api.SpendAsync(owner, 900_000);
        var ownersIncome = await _api.EarnAsync(owner, 900_000);
        var strangersIncome = await _api.EarnAsync(stranger, 900_000);

        (await _api.LinkAsync(stranger, strangersIncome.Id, ownersShoes.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.LinkAsync(stranger, ownersIncome.Id, ownersShoes.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.UnlinkAsync(stranger, ownersIncome.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LinkingADebit_OrToItself_Is422()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var coffee = await _api.SpendAsync(s, 50_000);
        var income = await _api.EarnAsync(s, 50_000);

        var debit = await _api.LinkAsync(s, coffee.Id, shoes.Id);
        debit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(debit)).Should().Be("REFUND_NOT_LINKABLE");

        var self = await _api.LinkAsync(s, income.Id, income.Id);
        self.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task LinkingMoreThanTheOriginal_Is422()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var big = await _api.EarnAsync(s, 1_000_000);

        var response = await _api.LinkAsync(s, big.Id, shoes.Id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("REFUND_EXCEEDS_ORIGINAL");
    }

    [Fact]
    public async Task OrphanRefund_ByPostTransactions_LowersTheCategory_AndIsNotIncome()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        await _api.SpendAsync(s, 900_000);

        var response = await _api.SendAsync(System.Net.Http.HttpMethod.Post, "/api/v1/transactions", s.Token, new
        {
            financialAccountId = s.Account, categoryId = s.Shopping, amountCents = 200_000, transactionType = "Credit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = "Cửa hàng cũ", relation = "Refund",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var orphan = await RefundApi.DataAsync(response);
        orphan.Relation.Should().Be("Refund");
        orphan.RelatedTransactionId.Should().BeNull();
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(700_000);
    }

    [Fact]
    public async Task OrphanRefund_WithIncomeCategory_IsRefused()
    {
        var s = await _api.StartAsync();
        var income = await _api.EarnAsync(s, 200_000);

        var response = await _api.LinkAsync(s, income.Id, null, "Refund", s.Income);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("REFUND_NOT_LINKABLE");
    }

    [Fact]
    public async Task ConfirmingADraft_WithTheOriginal_RecordsARefund_NotIncome()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        var draft = await _api.DraftCreditAsync(s, 900_000);
        var balance = await _api.BalanceAsync(s.Account);

        var response = await _api.ConfirmAsync(s, draft, new { relatedTransactionId = shoes.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refund = await RefundApi.DataAsync(response);
        refund.Status.Should().Be("Confirmed");
        refund.Relation.Should().Be("Refund");
        refund.CategoryId.Should().Be(s.Shopping);
        (await _api.BalanceAsync(s.Account)).Should().Be(balance + 900_000);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmingADraft_WithoutAnOriginal_StaysPlainIncome()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        await _api.SpendAsync(s, 900_000);
        var draft = await _api.DraftCreditAsync(s, 900_000);

        var confirmed = await RefundApi.DataAsync(await _api.ConfirmAsync(s, draft));

        confirmed.Relation.Should().BeNull();
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(900_000);
    }

    [Fact]
    public async Task LinkingADraft_ThenConfirming_KeepsTheLink()
    {
        var s = await _api.StartAsync();
        var budget = await _api.BudgetAsync(s, 1_000_000);
        var shoes = await _api.SpendAsync(s, 900_000);
        var draft = await _api.DraftCreditAsync(s, 300_000);

        (await _api.LinkAsync(s, draft, shoes.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(900_000, "nháp chưa tác động gì");
        (await _api.GetAsync(s, shoes.Id)).RefundedCents.Should().Be(0, "chỉ khoản hoàn đã xác nhận mới tính");

        await _api.ConfirmAsync(s, draft);

        (await _api.PeriodAsync(budget)).SpentCents.Should().Be(600_000);
    }

    [Fact]
    public async Task ConfirmingWithSomeoneElsesOriginal_Is404_AndTheDraftStaysDraft()
    {
        var owner = await _api.StartAsync();
        var stranger = await _api.StartAsync();
        var ownersShoes = await _api.SpendAsync(owner, 900_000);
        var draft = await _api.DraftCreditAsync(stranger, 900_000);

        var response = await _api.ConfirmAsync(stranger, draft, new { relatedTransactionId = ownersShoes.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.GetAsync(stranger, draft)).Status.Should().Be("Draft");
    }

    [Fact]
    public async Task ConfirmingWithADifferentCategory_Is422()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var draft = await _api.DraftCreditAsync(s, 900_000);

        var response = await _api.ConfirmAsync(s, draft, new { relatedTransactionId = shoes.Id, categoryId = s.Food });

        (await RefundApi.ErrorAsync(response)).Should().Be("REFUND_CATEGORY_LOCKED");
    }
}
