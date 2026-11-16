using System.Net;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 83: <c>POST /transactions/{id}/convert-to-transfer</c> và <c>not-transfer</c>. Bất biến số dư BR-TRF-07 qua API, khoản phí, gọi lại
/// (200), 404/422, ngân sách sau chuyển đổi, cờ bác bị xoá khi chuyển đổi (S2), cảnh báo ví Tiền mặt âm (S6), ảnh chụp, và hai request song song.
/// </summary>
[Collection("Integration")]
public class TransferConversionControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly TransferApi _api;

    public TransferConversionControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new TransferApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TwoDrafts_BecomeOneTransfer_AndBothWalletsMoveOnce()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000, description: "chuyen tien den vi MoMo");
        var credit = await _api.CreditAsync(w, 500_000, description: "Nap tien tu Vietcombank");

        var response = await _api.ConvertAsync(w.S, debit, w.Momo, credit);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await TransferApi.ConversionAsync(response);
        result.Transfer.Id.Should().Be(debit, "hai nháp: giữ chiều tiền ra");
        result.Transfer.TransactionType.Should().Be("Transfer");
        result.Transfer.FinancialAccountId.Should().Be(w.Vcb);
        result.Transfer.CounterAccountId.Should().Be(w.Momo);
        result.Transfer.AmountCents.Should().Be(500_000);
        result.Transfer.CategoryId.Should().BeNull();
        result.Transfer.Status.Should().Be("Confirmed");
        result.Fee.Should().BeNull();
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 500_000);
        (await _api.Base.BalanceAsync(w.Momo)).Should().Be(500_000);
        (await _api.Base.SendAsync(HttpMethod.Get, $"/api/v1/transactions/{credit}", w.S.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConfirmedExpense_LeavesTheBudget_WhenConverted()
    {
        var w = await _api.StartAsync();
        var budget = await _api.Base.BudgetAsync(w.S, 5_000_000, w.S.Food);
        var expense = await _api.Base.SpendAsync(w.S, 500_000, DateTimeOffset.UtcNow.AddMinutes(-5), w.S.Food, "nap MoMo");
        var credit = await _api.CreditAsync(w, 500_000);
        var cash = await _api.Base.BalanceAsync(w.Cash);
        (await _api.Base.PeriodAsync(budget)).SpentCents.Should().Be(500_000);

        (await _api.ConvertAsync(w.S, credit, w.Cash, expense.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.Base.PeriodAsync(budget)).SpentCents.Should().Be(0, "tiền chuyển giữa ví không phải chi tiêu");
        (await _api.Base.BalanceAsync(w.Cash)).Should().Be(cash, "khoản chi đã xác nhận đã trừ tiền mặt rồi");
        (await _api.Base.BalanceAsync(w.Momo)).Should().Be(500_000);
    }

    [Fact]
    public async Task Fee_BecomesItsOwnExpenseInOther_AndTheWalletEndsAsTheBankSaid()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 1_003_300, description: "nap vi MoMo");
        var credit = await _api.CreditAsync(w, 1_000_000);

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, credit, w.Vcb, debit));

        result.Transfer.AmountCents.Should().Be(1_000_000);
        result.Fee!.AmountCents.Should().Be(3_300);
        result.Fee.TransactionType.Should().Be("Debit");
        result.Fee.Description.Should().Be("Phí giao dịch");
        result.Fee.FinancialAccountId.Should().Be(w.Vcb);
        var other = await _api.Base.WithDbAsync(db => db.Categories.Where(c => c.UserId == null && c.Slug == "other").Select(c => c.Id).SingleAsync());
        result.Fee.CategoryId.Should().Be(other);
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 1_003_300);
        (await _api.Base.BalanceAsync(w.Momo)).Should().Be(1_000_000);
    }

    [Fact]
    public async Task ConvertingAgain_Returns200WithTheSameTransferAndFee_AndMovesNothing()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 1_003_300);
        var credit = await _api.CreditAsync(w, 1_000_000);
        var first = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, debit, w.Momo, credit));
        var vcb = await _api.Base.BalanceAsync(w.Vcb);

        var again = await _api.ConvertAsync(w.S, debit, w.Momo, credit);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await TransferApi.ConversionAsync(again);
        second.Transfer.Id.Should().Be(first.Transfer.Id);
        second.Fee!.Id.Should().Be(first.Fee!.Id);
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(vcb);
    }

    [Fact]
    public async Task AtmWithdrawal_Alone_MovesTheMoneyIntoCash()
    {
        var w = await _api.StartAsync();
        var atm = await _api.DebitAsync(w, 2_000_000, description: "RUT TIEN ATM");
        var cash = await _api.Base.BalanceAsync(w.Cash);

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, atm, w.Cash));

        result.Transfer.CounterAccountId.Should().Be(w.Cash);
        (await _api.Base.BalanceAsync(w.Cash)).Should().Be(cash + 2_000_000);
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 2_000_000);
    }

    [Fact]
    public async Task AtmDeposit_ThatTakesCashBelowZero_IsAllowedWithAWarning()
    {
        var w = await _api.StartAsync();
        var deposit = await _api.CreditAsync(w, 9_000_000, account: w.Vcb, description: "NOP TIEN TAI ATM");
        var cash = await _api.Base.BalanceAsync(w.Cash);

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, deposit, w.Cash));

        result.Warnings.Should().Equal("CASH_BALANCE_NEGATIVE");
        (await _api.Base.BalanceAsync(w.Cash)).Should().Be(cash - 9_000_000);
    }

    [Fact]
    public async Task ConversionClearsForecastExclusion()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000);
        await _api.Base.WithDbAsync(async db =>
        {
            var t = await db.Transactions.SingleAsync(x => x.Id == debit);
            t.ExcludeFromForecast = true;
            t.ForecastExclusionReason = ForecastExclusionReason.OneOff;
            return await db.SaveChangesAsync();
        });

        await _api.ConvertAsync(w.S, debit, w.Momo);

        var row = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == debit));
        row.ExcludeFromForecast.Should().BeFalse();
        row.ForecastExclusionReason.Should().BeNull();
    }

    [Fact]
    public async Task ADismissedSuggestion_CanStillBeConvertedByHand_AndTheFlagIsCleared()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000, description: "nap MoMo");
        var credit = await _api.CreditAsync(w, 500_000);
        (await _api.NotTransferAsync(w.S, debit)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.GetAsync(w.S, debit)).TransferSuggestion.Should().BeNull();

        (await _api.ConvertAsync(w.S, debit, w.Momo, credit)).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.Id == debit));
        row.TransferDismissedAt.Should().BeNull("S2: chuyển đổi thành công thì xoá cờ bác");
    }

    [Fact]
    public async Task NotTransfer_HidesTheSuggestionOnBothSides()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000, description: "nap MoMo");
        var credit = await _api.CreditAsync(w, 500_000);
        (await _api.GetAsync(w.S, debit)).TransferSuggestion!.PairedTransaction!.Id.Should().Be(credit);

        await _api.NotTransferAsync(w.S, debit);

        (await _api.GetAsync(w.S, debit)).TransferSuggestion.Should().BeNull();
        (await _api.GetAsync(w.S, credit)).TransferSuggestion.Should().BeNull();
        (await _api.NotTransferAsync(w.S, debit)).StatusCode.Should().Be(HttpStatusCode.OK, "gọi lại vẫn 200");
    }

    [Fact]
    public async Task SharedIntoAGroup_Is422SharedNotAllowed()
    {
        var w = await _api.StartAsync();
        var created = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/groups", w.S.Token, new { name = "Gia đình" });
        var group = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(created.Content, RefundApi.JsonOptions))!.Data.Id;
        var expense = await _api.Base.SpendAsync(w.S, 500_000, category: w.S.Food);
        (await _api.Base.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{expense.Id}/group", w.S.Token, new { groupId = group })).IsSuccessStatusCode.Should().BeTrue();

        var response = await _api.ConvertAsync(w.S, expense.Id, w.Momo);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("TRANSFER_SHARED_NOT_ALLOWED");
    }

    [Fact]
    public async Task SuspectedDuplicate_Is422NotAllowed()
    {
        var w = await _api.StartAsync();
        var other = await _api.DebitAsync(w, 500_000, status: TransactionStatus.Confirmed);
        var suspected = await _api.Dup.InsertAsync(w.S, 500_000, DateTimeOffset.UtcNow, account: w.Vcb, duplicateOf: other, duplicateStatus: DuplicateStatus.Suspected);

        var response = await _api.ConvertAsync(w.S, suspected, w.Momo);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("TRANSFER_NOT_ALLOWED");
    }

    [Fact]
    public async Task CounterAccountThatIsNotThePairedWallet_Is422PairMismatch()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000);
        var credit = await _api.CreditAsync(w, 500_000);

        var response = await _api.ConvertAsync(w.S, debit, w.Cash, credit);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("TRANSFER_PAIR_MISMATCH");
    }

    [Fact]
    public async Task FeeAboveTheLimit_Is422PairMismatch()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 600_000);
        var credit = await _api.CreditAsync(w, 500_000);

        var response = await _api.ConvertAsync(w.S, debit, w.Momo, credit);

        (await RefundApi.ErrorAsync(response)).Should().Be("TRANSFER_PAIR_MISMATCH");
    }

    [Fact]
    public async Task SameWallet_Is422SameAccount()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 500_000);

        var response = await _api.ConvertAsync(w.S, debit, w.Vcb);

        (await RefundApi.ErrorAsync(response)).Should().Be("TRANSACTION_TRANSFER_SAME_ACCOUNT");
    }

    [Fact]
    public async Task SomeoneElsesTransactionOrWallet_Is404()
    {
        var w = await _api.StartAsync();
        var stranger = await _api.StartAsync();
        var mine = await _api.DebitAsync(w, 500_000);
        var theirs = await _api.DebitAsync(stranger, 500_000);

        (await _api.ConvertAsync(w.S, theirs, w.Momo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.ConvertAsync(w.S, mine, stranger.Momo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.ConvertAsync(w.S, Guid.NewGuid(), w.Momo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.NotTransferAsync(w.S, theirs)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AConversion_WritesASnapshotOfTheRowsBefore()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 1_003_300);
        var credit = await _api.CreditAsync(w, 1_000_000);

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, debit, w.Momo, credit));

        var snapshot = await _api.Base.WithDbAsync(db => db.TransactionMergeSnapshots.AsNoTracking().SingleAsync(s => s.UserId == w.UserId));
        snapshot.Kind.Should().Be(MergeSnapshotKind.TransferPair);
        snapshot.SurvivorId.Should().Be(debit);
        snapshot.MergedId.Should().Be(credit);
        snapshot.Before.Should().Contain(credit.ToString()).And.Contain(result.Fee!.Id.ToString());
    }

    [Fact]
    public async Task TwoConversionsOfTheSamePairAtOnce_MoveTheMoneyOnce()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 1_003_300);
        var credit = await _api.CreditAsync(w, 1_000_000);

        var responses = await Task.WhenAll(_api.ConvertAsync(w.S, debit, w.Momo, credit), _api.ConvertAsync(w.S, credit, w.Vcb, debit));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 1_003_300);
        (await _api.Base.BalanceAsync(w.Momo)).Should().Be(1_000_000);
        var transfers = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().CountAsync(t => t.UserId == w.UserId && t.TransactionType == TransactionType.Transfer));
        transfers.Should().Be(1);
        var fees = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().CountAsync(t => t.UserId == w.UserId && t.Description == "Phí giao dịch"));
        fees.Should().Be(1);
    }

    // ---- BR-TRF-06: thông báo về sau một Transfer đã ghi tay ----

    [Fact]
    public async Task ABankDebitWithAFee_MergedIntoAManualTransfer_KeepsTheTransferAndAddsTheFee()
    {
        var w = await _api.StartAsync();
        var transfer = await _api.ManualTransferAsync(w, w.Vcb, w.Momo, 1_000_000);
        var bank = await _api.Dup.InsertAsync(w.S, 1_003_300, DateTimeOffset.UtcNow.AddMinutes(-4), account: w.Vcb,
            duplicateOf: transfer, duplicateStatus: DuplicateStatus.Suspected, description: "nap MoMo");

        var response = await _api.Dup.MergeAsync(w.S, bank, transfer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var survivor = await DuplicateApi.DataAsync(response);
        survivor.Id.Should().Be(transfer, "chuyển khoản ghi tay luôn được giữ (D9)");
        survivor.TransactionType.Should().Be("Transfer");
        survivor.MergedSources.Should().Equal("manual", "notification");
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000 - 1_003_300, "ví nguồn kết thúc đúng như ngân hàng báo");
        (await _api.Base.BalanceAsync(w.Momo)).Should().Be(1_000_000);
        var fee = await _api.Base.WithDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.UserId == w.UserId && t.Description == "Phí giao dịch"));
        fee.AmountCents.Should().Be(3_300);
    }
}

