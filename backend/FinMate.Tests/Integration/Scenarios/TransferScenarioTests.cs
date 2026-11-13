using System.Net;
using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Kịch bản đầu-cuối bắt buộc của Phase 83, qua đúng các endpoint app gọi (ngân sách tổng 5.000.000đ):
/// (a) VCB −500.000 "chuyen tien den vi MoMo" + MoMo +500.000 "Nap tien tu Vietcombank" → gợi ý ghép → chuyển đổi;
/// (b) VCB −2.000.000 "RUT TIEN ATM" → gợi ý ví Tiền mặt → chuyển đổi;
/// (c) VCB −1.003.300, MoMo +1.000.000 → Transfer 1.000.000 + "Phí giao dịch" 3.300;
/// (d) chuyển cho bạn và nhận từ người khác, không tín hiệu → không gợi ý;
/// (e) chat "nạp 500k vào momo" → Transfer → hai thông báo về → nghi trùng → gộp → vẫn một Transfer;
/// (f) từ chối gợi ý ở (a) → không còn gợi ý → xác nhận thành chi và thu thường.
/// </summary>
[Collection("Integration")]
public class TransferScenarioTests : IClassFixture<RefundApiFactory>
{
    private const string VcbPackage = "com.VCB";
    private const string MomoPackage = "com.mservice.momotransfer";
    private const long Start = 5_000_000;

    private readonly RefundApiFactory _factory;
    private readonly TransferApi _api;

    public TransferScenarioTests(RefundApiFactory factory)
    {
        _factory = factory;
        _api = new TransferApi(factory);
    }

    private record Analysis(Guid NotificationLogId, string Status, Guid? DraftTransactionId, Guid? DuplicateOfTransactionId);
    private record Monthly(long TotalSpentCents, long TotalIncomeCents);

    private sealed record Setup(TransferApi.Wallets W, Guid Budget, long CashStart);

    private async Task<Setup> StartAsync()
    {
        var w = await _api.StartAsync(vcbBalance: Start, momoBalance: Start);
        var response = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/budgets", w.S.Token, new CreateBudgetRequest(null, 5_000_000));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var budget = (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        return new Setup(w, budget, await _api.Base.BalanceAsync(w.Cash));
    }

    private async Task<Analysis> NotifyAsync(
        Setup x, string package, string type, long amount, string body, DateTimeOffset at, string? hint = null, string? kind = null)
    {
        FakeAIServiceClient.SetScenario(package, new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(amount, type, null, body, at, null, 0.95, hint, kind),
            new CategorizationResult(type == "debit" ? "shopping" : "income", 0.95),
            new DuplicateResult(false, null),
            new ModelVersions("1", "1", "1"),
            10));
        var response = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/notifications/analyze", x.W.S.Token, new
        {
            packageName = package,
            notificationTitle = package,
            notificationBody = $"{body} {Guid.NewGuid():N}",
            receivedAt = at,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;
    }

    private async Task<Monthly> MonthlyAsync(Setup x)
    {
        var response = await _api.Base.SendAsync(HttpMethod.Get, "/api/v1/reports/monthly-summary", x.W.S.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<Monthly>>(RefundApi.JsonOptions))!.Data;
    }

    private Task<List<DuplicateApi.TxData>> LiveAsync(Setup x) => _api.Dup.ListAsync(x.W.S, "limit=100");

    private List<string?> ActionsFor(Setup x) => _factory.Push.Sent.Where(p => p.UserId == x.W.UserId).Select(p => p.Action).ToList();

    private async Task<(Setup X, Guid Debit, Guid Credit)> TopUpNotificationsAsync(long debitAmount = 500_000, long creditAmount = 500_000)
    {
        var x = await StartAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        var debit = await NotifyAsync(x, VcbPackage, "debit", debitAmount, "chuyen tien den vi MoMo", at, "transfer", "wallet_topup");
        var credit = await NotifyAsync(x, MomoPackage, "credit", creditAmount, "Nap tien tu Vietcombank", at.AddMinutes(1), "transfer", "wallet_topup");
        return (x, debit.DraftTransactionId!.Value, credit.DraftTransactionId!.Value);
    }

    [Fact]
    public async Task A_BankDebitAndWalletCredit_AreSuggestedAsAPair_AndBecomeOneTransfer()
    {
        var (x, debit, credit) = await TopUpNotificationsAsync();
        var before = await MonthlyAsync(x);

        var suggestion = (await _api.GetAsync(x.W.S, credit)).TransferSuggestion!;
        suggestion.Kind.Should().Be("WalletTopup");
        suggestion.PairedTransaction!.Id.Should().Be(debit);
        suggestion.FeeCents.Should().Be(0);
        ActionsFor(x).Should().Contain("review_transfer");

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(x.W.S, credit, x.W.Vcb, debit));

        (await LiveAsync(x)).Should().ContainSingle().Which.TransactionType.Should().Be("Transfer");
        result.Transfer.FinancialAccountId.Should().Be(x.W.Vcb);
        result.Transfer.CounterAccountId.Should().Be(x.W.Momo);
        (await _api.Base.BalanceAsync(x.W.Vcb)).Should().Be(Start - 500_000);
        (await _api.Base.BalanceAsync(x.W.Momo)).Should().Be(Start + 500_000);
        (await MonthlyAsync(x)).Should().Be(before, "tổng chi và tổng thu tháng không đổi");
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task B_AtmWithdrawal_IsSuggestedIntoCash_AndMovesTheMoneyThere()
    {
        var x = await StartAsync();
        var atm = (await NotifyAsync(x, VcbPackage, "debit", 2_000_000, "RUT TIEN ATM 123 LE LOI", DateTimeOffset.UtcNow.AddMinutes(-2),
            "transfer", "atm_withdrawal")).DraftTransactionId!.Value;

        var suggestion = (await _api.GetAsync(x.W.S, atm)).TransferSuggestion!;
        suggestion.Kind.Should().Be("AtmWithdrawal");
        suggestion.PairedTransaction.Should().BeNull();
        suggestion.CandidateAccounts.Should().ContainSingle().Which.Id.Should().Be(x.W.Cash);

        await _api.ConvertAsync(x.W.S, atm, x.W.Cash);

        (await _api.Base.BalanceAsync(x.W.Cash)).Should().Be(x.CashStart + 2_000_000);
        (await _api.Base.BalanceAsync(x.W.Vcb)).Should().Be(Start - 2_000_000);
        (await MonthlyAsync(x)).TotalSpentCents.Should().Be(0);
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task C_ADifferenceBecomesAFeeInOther_AndBothWalletsEndAsTheBankSaid()
    {
        var (x, debit, credit) = await TopUpNotificationsAsync(1_003_300, 1_000_000);
        (await _api.GetAsync(x.W.S, debit)).TransferSuggestion!.FeeCents.Should().Be(3_300);

        var result = await TransferApi.ConversionAsync(await _api.ConvertAsync(x.W.S, debit, x.W.Momo, credit));

        result.Transfer.AmountCents.Should().Be(1_000_000);
        result.Fee!.AmountCents.Should().Be(3_300);
        result.Fee.Description.Should().Be("Phí giao dịch");
        var other = await _api.Base.WithDbAsync(db => db.Categories.Where(c => c.UserId == null && c.Slug == "other").Select(c => c.Id).SingleAsync());
        result.Fee.CategoryId.Should().Be(other);
        (await _api.Base.BalanceAsync(x.W.Vcb)).Should().Be(Start - 1_003_300, "BR-TRF-07: như khi xác nhận khoản chi của ngân hàng");
        (await _api.Base.BalanceAsync(x.W.Momo)).Should().Be(Start + 1_000_000);
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(3_300, "chỉ phí là chi tiêu");
    }

    [Fact]
    public async Task D_PayingAFriendAndBeingPaidBySomeoneElse_IsNeverSuggested()
    {
        var x = await StartAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);
        var debit = (await NotifyAsync(x, VcbPackage, "debit", 500_000, "chuyen tien cho ban Nam an trua", at)).DraftTransactionId!.Value;
        var credit = (await NotifyAsync(x, MomoPackage, "credit", 500_000, "Nhan tien tu Tran Thi B", at.AddMinutes(20))).DraftTransactionId!.Value;

        (await _api.GetAsync(x.W.S, debit)).TransferSuggestion.Should().BeNull();
        (await _api.GetAsync(x.W.S, credit)).TransferSuggestion.Should().BeNull();
        ActionsFor(x).Should().NotContain("review_transfer");
    }

    [Fact]
    public async Task E_ChatTransferThenBothBankNotifications_AreMergedIntoTheSameTransfer()
    {
        var x = await StartAsync();
        var text = $"nạp 500k vào momo {Guid.NewGuid():N}";
        FakeAIServiceClient.SetChat(text, new ChatParseResponse("transfer_money", "Bạn xác nhận chuyển nhé.",
            new ChatParseEntry("transfer", 500_000, "nạp momo", null, null, null, false, false), null, 5));
        var chat = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/transactions/chat", x.W.S.Token, new { text });
        chat.StatusCode.Should().Be(HttpStatusCode.OK);
        var transfer = await _api.ManualTransferAsync(x.W, x.W.Vcb, x.W.Momo, 500_000, DateTimeOffset.UtcNow.AddMinutes(-3));

        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        var debit = await NotifyAsync(x, VcbPackage, "debit", 500_000, "chuyen tien den vi MoMo", at, "transfer", "wallet_topup");
        var credit = await NotifyAsync(x, MomoPackage, "credit", 500_000, "Nap tien tu Vietcombank", at.AddMinutes(1), "transfer", "wallet_topup");

        debit.DuplicateOfTransactionId.Should().Be(transfer);
        credit.DuplicateOfTransactionId.Should().Be(transfer);
        (await _api.Dup.GetAsync(x.W.S, debit.DraftTransactionId!.Value)).DuplicateStatus.Should().Be("Suspected");
        (await _api.GetAsync(x.W.S, debit.DraftTransactionId!.Value)).TransferSuggestion.Should().BeNull("nháp đang nghi trùng không mang gợi ý chuyển ví");

        (await _api.Dup.MergeAsync(x.W.S, debit.DraftTransactionId!.Value, transfer)).StatusCode.Should().Be(HttpStatusCode.OK);
        var merged = await DuplicateApi.DataAsync(await _api.Dup.MergeAsync(x.W.S, credit.DraftTransactionId!.Value, transfer));

        merged.Id.Should().Be(transfer);
        merged.MergedSources.Should().Equal("manual", "notification");
        (await LiveAsync(x)).Should().ContainSingle().Which.Id.Should().Be(transfer);
        (await _api.Base.BalanceAsync(x.W.Vcb)).Should().Be(Start - 500_000, "không trừ hai lần");
        (await _api.Base.BalanceAsync(x.W.Momo)).Should().Be(Start + 500_000, "không cộng hai lần");
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task F_DismissingTheSuggestion_LeavesPlainSpendingAndIncome()
    {
        var (x, debit, credit) = await TopUpNotificationsAsync();

        (await _api.NotTransferAsync(x.W.S, debit)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _api.GetAsync(x.W.S, debit)).TransferSuggestion.Should().BeNull();
        (await _api.GetAsync(x.W.S, credit)).TransferSuggestion.Should().BeNull();
        (await _api.Base.ConfirmAsync(x.W.S, debit, new { categoryId = x.W.S.Shopping })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.Base.ConfirmAsync(x.W.S, credit, new { categoryId = x.W.S.Income })).StatusCode.Should().Be(HttpStatusCode.OK);

        var monthly = await MonthlyAsync(x);
        monthly.TotalSpentCents.Should().Be(500_000);
        monthly.TotalIncomeCents.Should().Be(500_000);
        (await _api.Base.BalanceAsync(x.W.Vcb)).Should().Be(Start - 500_000);
        (await _api.Base.BalanceAsync(x.W.Momo)).Should().Be(Start + 500_000);
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(500_000);
    }
}
