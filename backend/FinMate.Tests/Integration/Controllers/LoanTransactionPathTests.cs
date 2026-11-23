using System.Net;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Sổ vay trên các đường giao dịch có sẵn (Phase 90, Task 7): xoá (BR-DEBT-09, D5), sửa (D3), gộp trùng và chuyển ví (D7), xác nhận kèm khoản (D4, S1),
/// gộp một thông báo ngân hàng với lần trả đã ghi qua ví (S4, kịch bản h) kể cả khi lần trả đã tách lãi (Q14).
/// </summary>
[Collection("Integration")]
public class LoanTransactionPathTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LoanTransactionPathTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    private static async Task<Guid> LoanAsync(LoanTestApi api, string direction, long principal, Guid? wallet = null, decimal rate = 0, DateOnly? start = null)
        => (await api.ExpectAsync(HttpMethod.Post, "/api/v1/loans", new
        {
            direction, title = "Khoản", counterparty = "Nam", principalCents = principal, interestRatePercentYear = rate, startDate = start,
            createTransaction = wallet is null ? null : new { financialAccountId = wallet },
        }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<IReadOnlyList<(string Kind, long Amount, Guid? Transaction, Guid? Interest)>> EntriesAsync(LoanTestApi api, Guid loan)
        => (await api.ExpectAsync(HttpMethod.Get, $"/api/v1/loans/{loan}", null, HttpStatusCode.OK)).GetProperty("entries").EnumerateArray()
            .Select(e => (
                e.GetProperty("kind").GetString()!, e.GetProperty("amountCents").GetInt64(),
                e.GetProperty("transactionId").ValueKind == System.Text.Json.JsonValueKind.Null ? (Guid?)null : e.GetProperty("transactionId").GetGuid(),
                e.GetProperty("interestTransactionId").ValueKind == System.Text.Json.JsonValueKind.Null ? (Guid?)null : e.GetProperty("interestTransactionId").GetGuid()))
            .ToList();

    private static async Task<long> RemainingAsync(LoanTestApi api, Guid loan)
        => (await api.ExpectAsync(HttpMethod.Get, $"/api/v1/loans/{loan}", null, HttpStatusCode.OK)).GetProperty("loan").GetProperty("remainingCents").GetInt64();

    /// <summary>Khoản cho vay 1tr từ ví, kèm một lần thu 400k vào ví. Trả (khoản, giao dịch giải ngân, giao dịch thu).</summary>
    private static async Task<(Guid Loan, Guid Principal, Guid Repayment)> LentWithPaymentAsync(LoanTestApi api, Guid wallet)
    {
        var id = await LoanAsync(api, "lend", 1_000_000, wallet);
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 400_000, financialAccountId = wallet }, HttpStatusCode.OK);
        var entries = await EntriesAsync(api, id);
        return (id, entries.Single(e => e.Kind == "principal").Transaction!.Value, entries.Single(e => e.Kind == "repayment").Transaction!.Value);
    }

    private static object UpdateBody(Guid wallet, Guid? category, long amount, string type, DateTimeOffset at)
        => new { financialAccountId = wallet, categoryId = category, amountCents = amount, transactionType = type, transactedAt = at };

    private static Task<Guid> DraftAsync(LoanTestApi api, Guid wallet, long amount, TransactionType type, TransactionSource source = TransactionSource.Manual)
        => api.InDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = api.UserId, FinancialAccountId = wallet, AmountCents = amount, TransactionType = type, Source = source,
                Status = TransactionStatus.Draft, MerchantName = "NAM TRA NO", TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });

    [Fact]
    public async Task DeletingALinkedRepayment_RemovesItsEntry_AndRestoresTheRemaining()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var (loan, _, repayment) = await LentWithPaymentAsync(api, vcb);

        await api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{repayment}", null, HttpStatusCode.NoContent);

        (await RemainingAsync(api, loan)).Should().Be(1_000_000);
        (await api.BalanceAsync(vcb)).Should().Be(9_000_000);
        (await api.InDbAsync(db => db.LoanRepayments.SingleAsync(e => e.TransactionId == repayment))).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeletingTheOnlyPrincipal_WithPayments_Is422_AndNothingChanges()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var (loan, principal, _) = await LentWithPaymentAsync(api, vcb);

        (await api.ErrorCodeAsync(HttpMethod.Delete, $"/api/v1/transactions/{principal}", null, HttpStatusCode.UnprocessableEntity)).Should().Be("LOAN_PRINCIPAL_BELOW_PAID");

        (await api.TransactionDetailAsync(principal)).GetProperty("id").GetGuid().Should().Be(principal);
        (await api.BalanceAsync(vcb)).Should().Be(9_400_000);
        (await RemainingAsync(api, loan)).Should().Be(600_000);
    }

    [Fact]
    public async Task DeletingTheOnlyPrincipal_WithoutPayments_DeletesTheLoan()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var id = await LoanAsync(api, "lend", 1_000_000, vcb);
        var principal = (await EntriesAsync(api, id)).Single().Transaction!.Value;

        await api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{principal}", null, HttpStatusCode.NoContent);

        (await api.SendAsync(HttpMethod.Get, $"/api/v1/loans/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000);
    }

    [Fact]
    public async Task EditingALinkedTransaction_SyncsAmountAndDate_ButNotCategoryOrType()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var (loan, _, repayment) = await LentWithPaymentAsync(api, vcb);
        var loanCategory = await api.CategoryAsync("loan");
        var at = DateTimeOffset.UtcNow.AddDays(-1);

        (await api.ErrorCodeAsync(HttpMethod.Patch, $"/api/v1/transactions/{repayment}", UpdateBody(vcb, loanCategory, 1_100_000, "Credit", at), HttpStatusCode.UnprocessableEntity))
            .Should().Be("LOAN_REPAYMENT_EXCEEDS_REMAINING");
        (await api.ErrorCodeAsync(HttpMethod.Patch, $"/api/v1/transactions/{repayment}", UpdateBody(vcb, await api.CategoryAsync("income"), 400_000, "Credit", at), HttpStatusCode.UnprocessableEntity))
            .Should().Be("TRANSACTION_LOAN_LINKED");
        (await api.ErrorCodeAsync(HttpMethod.Patch, $"/api/v1/transactions/{repayment}", UpdateBody(vcb, loanCategory, 400_000, "Debit", at), HttpStatusCode.UnprocessableEntity))
            .Should().Be("TRANSACTION_LOAN_LINKED");

        await api.ExpectAsync(HttpMethod.Patch, $"/api/v1/transactions/{repayment}", UpdateBody(vcb, loanCategory, 1_000_000, "Credit", at), HttpStatusCode.OK);

        var detail = await api.ExpectAsync(HttpMethod.Get, $"/api/v1/loans/{loan}", null, HttpStatusCode.OK);
        detail.GetProperty("loan").GetProperty("status").GetString().Should().Be("settled");
        var entry = detail.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "repayment");
        entry.GetProperty("amountCents").GetInt64().Should().Be(1_000_000);
        entry.GetProperty("at").GetDateTimeOffset().Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000);
    }

    [Fact]
    public async Task ConvertingALinkedTransactionToATransfer_Is422()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var cash = await api.WalletAsync("Tiền mặt", 0);
        var (_, principal, _) = await LentWithPaymentAsync(api, vcb);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"/api/v1/transactions/{principal}/convert-to-transfer", new { counterAccountId = cash }, HttpStatusCode.UnprocessableEntity))
            .Should().Be("TRANSACTION_LOAN_LINKED");
    }

    [Fact]
    public async Task MergingAwayALinkedTransaction_MovesTheLinkToTheSurvivor_InLoan()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var (loan, _, repayment) = await LentWithPaymentAsync(api, vcb);
        var other = await api.TransactionAsync(vcb, await api.CategoryAsync("income"), 400_000, "Credit");

        var survivor = (await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{repayment}/merge", new { withTransactionId = other }, HttpStatusCode.OK))
            .GetProperty("id").GetGuid();

        (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment").Transaction.Should().Be(survivor);
        (await api.TransactionDetailAsync(survivor)).GetProperty("categoryId").GetGuid().Should().Be(await api.CategoryAsync("loan"));
        (await RemainingAsync(api, loan)).Should().Be(600_000);
    }

    [Fact]
    public async Task MergingTwoLinkedTransactions_Is422()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var (_, _, repayment) = await LentWithPaymentAsync(api, vcb);
        var otherLoan = await LoanAsync(api, "lend", 900_000);
        var other = await api.TransactionAsync(vcb, await api.CategoryAsync("income"), 400_000, "Credit");
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{otherLoan}/repayments", new { transactionId = other }, HttpStatusCode.OK);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"/api/v1/transactions/{repayment}/merge", new { withTransactionId = other }, HttpStatusCode.UnprocessableEntity))
            .Should().Be("TRANSACTION_LOAN_LINKED");
    }

    /// <summary>S4, kịch bản (h): lần trả ghi qua ví, rồi thông báo ngân hàng cùng số tiền về → gộp → liên kết ở bản giữ lại, không trừ hai lần.</summary>
    [Fact]
    public async Task ScenarioH_ARepaymentThroughAWallet_ThenTheBankNotification_MergesWithoutCountingTwice()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var loan = await LoanAsync(api, "borrow", 2_000_000);
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{loan}/repayments", new { amountCents = 500_000, financialAccountId = vcb }, HttpStatusCode.OK);
        var manual = (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment").Transaction!.Value;
        var notice = await DraftAsync(api, vcb, 500_000, TransactionType.Debit, TransactionSource.Notification);

        var survivor = (await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{notice}/merge", new { withTransactionId = manual }, HttpStatusCode.OK))
            .GetProperty("id").GetGuid();

        (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment").Should().Be(("repayment", 500_000L, (Guid?)survivor, (Guid?)null));
        (await RemainingAsync(api, loan)).Should().Be(1_500_000, "paidCents không đổi");
        (await api.BalanceAsync(vcb)).Should().Be(9_500_000, "không trừ hai lần");
    }

    /// <summary>
    /// Q14: trả 2.060.000 qua ví cho khoản 2tr lãi 12% sau 90 ngày (tách 2.000.000 + 60.000 lãi), rồi thông báo ngân hàng 2.060.000 về: gộp được (số
    /// tiền ngân hàng bằng nhau), bản giữ lại mang phần gốc, giao dịch lãi ở lại, số dư trừ đúng 2.060.000 một lần.
    /// </summary>
    [Fact]
    public async Task Q14_ASplitRepayment_ThenTheBankNotificationOfTheFullAmount_Merges()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var loan = await LoanAsync(api, "borrow", 2_000_000, rate: 12, start: today.AddDays(-90));
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{loan}/repayments", new { amountCents = 2_060_000, financialAccountId = vcb }, HttpStatusCode.OK);
        var (_, _, manual, interest) = (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment");
        var notice = await DraftAsync(api, vcb, 2_060_000, TransactionType.Debit, TransactionSource.Notification);

        var survivor = (await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{notice}/merge", new { withTransactionId = manual }, HttpStatusCode.OK))
            .GetProperty("id").GetGuid();

        var entry = (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment");
        entry.Should().Be(("repayment", 2_000_000L, (Guid?)survivor, interest));
        (await api.TransactionDetailAsync(survivor)).GetProperty("amountCents").GetInt64().Should().Be(2_000_000);
        (await api.TransactionDetailAsync(interest!.Value)).GetProperty("amountCents").GetInt64().Should().Be(60_000);
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000 - 2_060_000);
        (await RemainingAsync(api, loan)).Should().Be(0);
    }

    [Fact]
    public async Task ConfirmingADraftWithALoanId_ConfirmsLinksAndMovesToLoan_InOneStep()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var loan = await LoanAsync(api, "lend", 2_000_000);
        var draft = await DraftAsync(api, vcb, 500_000, TransactionType.Credit);

        var confirmed = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { categoryId = await api.CategoryAsync("income"), loanId = loan }, HttpStatusCode.OK);

        confirmed.GetProperty("status").GetString().Should().Be("Confirmed");
        confirmed.GetProperty("categoryId").GetGuid().Should().Be(await api.CategoryAsync("loan"));
        (await RemainingAsync(api, loan)).Should().Be(1_500_000);
        (await api.BalanceAsync(vcb)).Should().Be(10_500_000);
    }

    [Fact]
    public async Task ConfirmingWithALoanId_OverTheRemainingWithinInterest_SplitsTheInterestOff()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var loan = await LoanAsync(api, "lend", 2_000_000, rate: 12, start: today.AddDays(-90));
        var draft = await DraftAsync(api, vcb, 2_060_000, TransactionType.Credit, TransactionSource.Notification);

        var confirmed = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = loan }, HttpStatusCode.OK);

        confirmed.GetProperty("amountCents").GetInt64().Should().Be(2_000_000);
        var (_, _, _, interest) = (await EntriesAsync(api, loan)).Single(e => e.Kind == "repayment");
        var interestTx = await api.TransactionDetailAsync(interest!.Value);
        (interestTx.GetProperty("amountCents").GetInt64(), interestTx.GetProperty("categoryId").GetGuid()).Should().Be((60_000L, await api.CategoryAsync("income")));
        (await api.BalanceAsync(vcb)).Should().Be(12_060_000);
        (await api.MonthlySummaryAsync()).GetProperty("totalIncomeCents").GetInt64().Should().Be(60_000, "tiền lãi nhận được là thu nhập thật (S1, Q16)");
    }

    [Fact]
    public async Task ConfirmingWithALoanId_ThatOverpays_Is422_AndLeavesTheDraft()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var loan = await LoanAsync(api, "lend", 300_000);
        var draft = await DraftAsync(api, vcb, 500_000, TransactionType.Credit);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = loan }, HttpStatusCode.UnprocessableEntity))
            .Should().Be("LOAN_REPAYMENT_EXCEEDS_REMAINING");
        (await api.ErrorCodeAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = loan, groupId = Guid.NewGuid() }, HttpStatusCode.UnprocessableEntity))
            .Should().Be("LOAN_TRANSACTION_NOT_ELIGIBLE");
        (await api.TransactionDetailAsync(draft)).GetProperty("status").GetString().Should().Be("Draft");
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000);
        (await api.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
