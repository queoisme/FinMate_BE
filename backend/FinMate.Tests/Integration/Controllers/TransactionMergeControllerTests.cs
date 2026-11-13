using System.Net;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 82: <c>POST /transactions/{id}/merge</c>. Bản giữ lại, số dư hai ví, ngân sách, bản bị gộp xoá mềm kèm liên kết, gộp lại cùng cặp (200),
/// 404 / 422, và hai lượt gộp hay gộp + xác nhận chạy cùng lúc (BR-DUP-09…14, D11).
/// </summary>
[Collection("Integration")]
public class TransactionMergeControllerTests : IClassFixture<AuthApiFactory>
{
    private const long Price = 50_000;
    private readonly DuplicateApi _api;

    public TransactionMergeControllerTests(AuthApiFactory factory)
    {
        _api = new DuplicateApi(factory);
    }

    private sealed record Pair(RefundApi.Session S, Guid Vcb, Guid Chat, Guid Bank, Guid Budget, DateTimeOffset BankAt);

    /// <summary>Chat "trà sữa 50k" vào ví tiền mặt (đã xác nhận, đã tính ngân sách) rồi nháp thông báo VCB nghi trùng với nó.</summary>
    private async Task<Pair> ChatThenBankAsync()
    {
        var s = await _api.Base.StartAsync();
        var vcb = await _api.Base.CreateAccountAsync(s.Token, "VCB", 2_000_000);
        var budget = await _api.Base.BudgetAsync(s, 1_000_000, s.Food);
        var chat = await _api.Base.SpendAsync(s, Price, DateTimeOffset.UtcNow.AddMinutes(-2), s.Food, "trà sữa");
        var bankAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var bank = await _api.InsertAsync(s, Price, bankAt, account: vcb, category: s.Shopping, duplicateOf: chat.Id,
            duplicateStatus: DuplicateStatus.Suspected, merchant: "PHUC LONG");
        return new Pair(s, vcb, chat.Id, bank, budget, bankAt);
    }

    [Fact]
    public async Task ChatAndBank_BecomeOneTransaction_OnTheBankWallet_WithTheChatCategory()
    {
        var p = await ChatThenBankAsync();
        var cashBefore = await _api.Base.BalanceAsync(p.S.Account);
        var vcbBefore = await _api.Base.BalanceAsync(p.Vcb);

        var response = await _api.MergeAsync(p.S, p.Bank, p.Chat);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var survivor = await DuplicateApi.DataAsync(response);
        survivor.Id.Should().Be(p.Chat, "đúng một bản đã xác nhận thì giữ bản đó");
        survivor.Status.Should().Be("Confirmed");
        survivor.FinancialAccountId.Should().Be(p.Vcb);
        survivor.TransactedAt.Should().BeCloseTo(p.BankAt, TimeSpan.FromMilliseconds(1));
        survivor.CategoryId.Should().Be(p.S.Food);
        survivor.MerchantName.Should().Be("trà sữa");
        survivor.MergedSources.Should().Equal("manual", "notification");
        survivor.DuplicateStatus.Should().BeNull();

        (await _api.Base.BalanceAsync(p.S.Account)).Should().Be(cashBefore + Price, "ví tiền mặt được trả lại");
        (await _api.Base.BalanceAsync(p.Vcb)).Should().Be(vcbBefore - Price);
        (await _api.Base.PeriodAsync(p.Budget)).SpentCents.Should().Be(Price, "vẫn chỉ một khoản chi");

        (await _api.Base.SendAsync(HttpMethod.Get, $"/api/v1/transactions/{p.Bank}", p.S.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var loser = await _api.Base.WithDbAsync(db => db.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == p.Bank));
        loser.DeletedAt.Should().NotBeNull();
        loser.MergedIntoTransactionId.Should().Be(p.Chat);
    }

    [Fact]
    public async Task MergingTheSamePairAgain_Returns200WithTheSameSurvivor_InEitherOrder()
    {
        var p = await ChatThenBankAsync();
        await _api.MergeAsync(p.S, p.Bank, p.Chat);
        var vcb = await _api.Base.BalanceAsync(p.Vcb);

        var again = await _api.MergeAsync(p.S, p.Chat, p.Bank);
        var reversed = await _api.MergeAsync(p.S, p.Bank, p.Chat);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DuplicateApi.DataAsync(again)).Id.Should().Be(p.Chat);
        (await DuplicateApi.DataAsync(reversed)).Id.Should().Be(p.Chat);
        (await _api.Base.BalanceAsync(p.Vcb)).Should().Be(vcb, "gộp lại không di chuyển tiền lần nữa");
    }

    [Fact]
    public async Task TwoDrafts_KeepTheNotification_AndConfirmItOnce()
    {
        var s = await _api.Base.StartAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-3);
        var first = await _api.InsertAsync(s, 35_000, at, category: s.Food);
        var second = await _api.InsertAsync(s, 35_000, at.AddMinutes(2), duplicateOf: first, duplicateStatus: DuplicateStatus.Suspected);
        var before = await _api.Base.BalanceAsync(s.Account);

        var survivor = await DuplicateApi.DataAsync(await _api.MergeAsync(s, second, first));

        survivor.Id.Should().Be(first);
        survivor.Status.Should().Be("Confirmed");
        (await _api.Base.BalanceAsync(s.Account)).Should().Be(before - 35_000);
    }

    [Fact]
    public async Task SomeoneElsesOrUnknown_Is404()
    {
        var p = await ChatThenBankAsync();
        var stranger = await _api.Base.StartAsync();
        var mine = await _api.InsertAsync(stranger, Price, DateTimeOffset.UtcNow);

        (await _api.MergeAsync(stranger, mine, p.Chat)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.MergeAsync(p.S, p.Bank, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DifferentAmount_Is422()
    {
        var s = await _api.Base.StartAsync();
        var a = await _api.InsertAsync(s, 50_000, DateTimeOffset.UtcNow);
        var b = await _api.InsertAsync(s, 50_001, DateTimeOffset.UtcNow, TransactionSource.Manual);

        var response = await _api.MergeAsync(s, a, b);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("MERGE_NOT_ALLOWED");
    }

    [Fact]
    public async Task ConfirmedLoserWithARefund_Is422()
    {
        var s = await _api.Base.StartAsync();
        var older = await _api.Base.SpendAsync(s, 300_000);
        var newer = await _api.Base.SpendAsync(s, 300_000);
        (await _api.Base.RefundAsync(s, newer.Id, new { amountCents = 100_000 })).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _api.MergeAsync(s, older.Id, newer.Id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("MERGE_NOT_ALLOWED");
    }

    [Fact]
    public async Task AlreadyMergedIntoAThirdTransaction_Is422()
    {
        var p = await ChatThenBankAsync();
        await _api.MergeAsync(p.S, p.Bank, p.Chat);
        var third = await _api.InsertAsync(p.S, Price, DateTimeOffset.UtcNow, account: p.Vcb);

        var response = await _api.MergeAsync(p.S, p.Bank, third);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("MERGE_NOT_ALLOWED");
    }

    [Fact]
    public async Task OtherSuspectedDraftsPointingAtTheLoser_NowPointAtTheSurvivor()
    {
        var p = await ChatThenBankAsync();
        var third = await _api.InsertAsync(p.S, Price, DateTimeOffset.UtcNow, account: p.Vcb, duplicateOf: p.Bank, duplicateStatus: DuplicateStatus.Suspected);

        await _api.MergeAsync(p.S, p.Bank, p.Chat);

        var dto = await _api.GetAsync(p.S, third);
        dto.DuplicateStatus.Should().Be("Suspected");
        dto.DuplicateOf!.Id.Should().Be(p.Chat);
    }

    // ---- đồng thời (BR-DUP-14, D11) ----

    [Fact]
    public async Task TwoMergesOfTheSamePairAtOnce_LeaveOneSurvivor_AndMoveMoneyOnce()
    {
        var p = await ChatThenBankAsync();
        var vcb = await _api.Base.BalanceAsync(p.Vcb);
        var cash = await _api.Base.BalanceAsync(p.S.Account);

        var responses = await Task.WhenAll(_api.MergeAsync(p.S, p.Bank, p.Chat), _api.MergeAsync(p.S, p.Chat, p.Bank));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await _api.Base.BalanceAsync(p.Vcb)).Should().Be(vcb - Price);
        (await _api.Base.BalanceAsync(p.S.Account)).Should().Be(cash + Price);
        (await _api.Base.PeriodAsync(p.Budget)).SpentCents.Should().Be(Price);
    }

    [Fact]
    public async Task CrossedMerges_NeverMergeOneRowTwice()
    {
        var s = await _api.Base.StartAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        var a = await _api.InsertAsync(s, Price, at);
        var b = await _api.InsertAsync(s, Price, at.AddMinutes(1));
        var c = await _api.InsertAsync(s, Price, at.AddMinutes(2));
        var before = await _api.Base.BalanceAsync(s.Account);

        var responses = await Task.WhenAll(_api.MergeAsync(s, a, b), _api.MergeAsync(s, b, c));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().BeGreaterThanOrEqualTo(1);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var live = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().Where(t => t.Id == a || t.Id == b || t.Id == c).ToListAsync());
        var confirmed = live.Count(t => t.Status == TransactionStatus.Confirmed);
        (await _api.Base.BalanceAsync(s.Account)).Should().Be(before - confirmed * Price, "mỗi khoản còn sống và đã xác nhận trừ ví đúng một lần");
        var merged = await _api.Base.WithDbAsync(db => db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => (t.Id == a || t.Id == b || t.Id == c) && t.MergedIntoTransactionId != null).ToListAsync());
        merged.Should().OnlyContain(t => t.DeletedAt != null);
        live.Select(t => t.Id).Should().NotIntersectWith(merged.Select(t => t.Id));
    }

    [Fact]
    public async Task MergeRacingAConfirmOfTheLoser_NeverLeavesADeletedConfirmedRow()
    {
        var s = await _api.Base.StartAsync();
        var vcb = await _api.Base.CreateAccountAsync(s.Token, "VCB", 2_000_000);
        var older = await _api.InsertAsync(s, Price, DateTimeOffset.UtcNow.AddMinutes(-3), category: s.Food, account: vcb);
        var newer = await _api.InsertAsync(s, Price, DateTimeOffset.UtcNow.AddMinutes(-2), category: s.Food, account: vcb,
            duplicateOf: older, duplicateStatus: DuplicateStatus.Suspected);
        var before = await _api.Base.BalanceAsync(vcb);

        await Task.WhenAll(_api.MergeAsync(s, newer, older), _api.Base.ConfirmAsync(s, newer));

        var rows = await _api.Base.WithDbAsync(db => db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == older || t.Id == newer).ToListAsync());
        rows.Count(t => t.DeletedAt == null).Should().Be(1, "gộp hay xác nhận chạy trước thì cuối cùng vẫn còn đúng một khoản");
        var liveConfirmed = rows.Count(t => t.DeletedAt == null && t.Status == TransactionStatus.Confirmed);
        (await _api.Base.BalanceAsync(vcb)).Should().Be(before - liveConfirmed * Price, "ví chỉ bị trừ cho khoản còn sống");
    }
}
