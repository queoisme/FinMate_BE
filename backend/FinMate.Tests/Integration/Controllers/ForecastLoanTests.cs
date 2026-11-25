using System.Net;
using System.Text.Json;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Sổ vay trong các API dự báo (Phase 90, Task 9; chép từ bản sổ nợ Phase 86 cũ): BR-DEBT-15, 16, 17, D12, D13, S5.</summary>
[Collection("Integration")]
public class ForecastLoanTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastLoanTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    private static Task<JsonElement> DebtAsync(LoanTestApi api, string name, string direction, long principal, DateOnly? due)
        => api.ExpectAsync(HttpMethod.Post, "/api/v1/loans", new { direction, title = $"Khoản {name}", counterparty = name, principalCents = principal, dueDate = due }, HttpStatusCode.Created);

    private static Task<JsonElement> CashflowAsync(LoanTestApi api, DateOnly to)
        => api.ExpectAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={to:yyyy-MM-dd}", null, HttpStatusCode.OK);

    private static JsonElement Day(JsonElement cashflow, DateOnly date)
        => cashflow.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == date.ToString("yyyy-MM-dd"));

    private static JsonElement DayBefore(JsonElement cashflow, DateOnly date) => Day(cashflow, date.AddDays(-1));

    [Fact]
    public async Task ABorrowedDebt_LeavesAllThreeLinesOnItsDueDate()
    {
        var api = await UserAsync();
        await api.WalletAsync(balance: 10_000_000);
        var today = VietnamTime.Today();
        var due = today.AddDays(5);
        await DebtAsync(api, "Chị Hoa", "borrow", 3_000_000, due);

        var cashflow = await CashflowAsync(api, today.AddDays(10));

        var before = DayBefore(cashflow, due);
        var on = Day(cashflow, due);
        on.GetProperty("flexibleSpendCents").GetInt64().Should().Be(0, "người dùng mới chưa có lịch sử chi");
        foreach (var line in new[] { "expectedBalanceCents", "lowBalanceCents", "highBalanceCents" })
        {
            (before.GetProperty(line).GetInt64() - on.GetProperty(line).GetInt64()).Should().Be(3_000_000, line);
        }

        var evt = on.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "loan_outflow");
        evt.GetProperty("amountCents").GetInt64().Should().Be(-3_000_000);
        evt.GetProperty("optimisticOnly").GetBoolean().Should().BeFalse();
        cashflow.GetProperty("mainCauses").EnumerateArray().Should().Contain(c => c.GetProperty("kind").GetString() == "loan_outflow"
            && c.GetProperty("source").GetString() == "user_entered");
    }

    [Fact]
    public async Task ALentDebt_OnlyLiftsTheOptimisticLine()
    {
        var api = await UserAsync();
        await api.WalletAsync(balance: 10_000_000);
        var today = VietnamTime.Today();
        var due = today.AddDays(4);
        await DebtAsync(api, "Lan", "lend", 1_000_000, due);

        var cashflow = await CashflowAsync(api, today.AddDays(10));

        var evt = Day(cashflow, due).GetProperty("events").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "loan_inflow");
        evt.GetProperty("optimisticOnly").GetBoolean().Should().BeTrue();
        (cashflow.GetProperty("endHighBalanceCents").GetInt64() - cashflow.GetProperty("endBalanceCents").GetInt64()).Should().Be(1_000_000);
        cashflow.GetProperty("mainCauses").EnumerateArray().Should().NotContain(c => c.GetProperty("kind").GetString() == "loan_inflow");
    }

    [Fact]
    public async Task Spendable_TakesOutTheBorrowedDebtDue_AndListsUndatedDebts()
    {
        var api = await UserAsync();
        await api.WalletAsync(balance: 10_000_000);
        var before = (await api.ExpectAsync(HttpMethod.Get, "/api/v1/forecast/spendable", null, HttpStatusCode.OK)).GetProperty("spendableCents").GetInt64();
        await DebtAsync(api, "Chị Hoa", "borrow", 3_000_000, VietnamTime.Today());
        await DebtAsync(api, "Bạn cũ", "borrow", 700_000, null);

        var spendable = await api.ExpectAsync(HttpMethod.Get, "/api/v1/forecast/spendable", null, HttpStatusCode.OK);

        spendable.GetProperty("loansDueCents").GetInt64().Should().Be(3_000_000);
        spendable.GetProperty("spendableCents").GetInt64().Should().Be(before - 3_000_000);
        spendable.GetProperty("loans").EnumerateArray().Single().GetProperty("label").GetString().Should().Be("Trả khoản vay", "không tên người");
        spendable.GetProperty("undatedLoans").GetProperty("count").GetInt32().Should().Be(1);
        spendable.GetProperty("undatedLoans").GetProperty("iOweCents").GetInt64().Should().Be(700_000);
    }

    [Fact]
    public async Task PurchaseCheck_WarnsAboutUndatedDebts()
    {
        var api = await UserAsync();
        await api.WalletAsync(balance: 10_000_000);
        await DebtAsync(api, "Bạn cũ", "borrow", 700_000, null);

        var check = await api.ExpectAsync(HttpMethod.Post, "/api/v1/forecast/purchase-check", new { amountCents = 100_000, item = "Cà phê" }, HttpStatusCode.OK);

        check.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("undated_loan");
    }

    [Fact]
    public async Task UnlinkedLoanTransactions_Warn_WithoutChangingNumbers()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var today = VietnamTime.Today();
        var before = await CashflowAsync(api, today.AddDays(10));
        (before.TryGetProperty("unlinkedLoanTransactions", out var none) && none.ValueKind != JsonValueKind.Null).Should().BeFalse();

        var loan = await api.CategoryAsync("loan");
        await api.TransactionAsync(vcb, loan, 500_000, "Credit");
        var linkedLater = await api.TransactionAsync(vcb, loan, 200_000, "Credit");
        var debt = (await DebtAsync(api, "Nam", "lend", 1_000_000, null)).GetProperty("id").GetGuid();
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{debt}/repayments", new { transactionId = linkedLater }, HttpStatusCode.OK);

        var cashflow = await CashflowAsync(api, today.AddDays(10));
        var spendable = await api.ExpectAsync(HttpMethod.Get, "/api/v1/forecast/spendable", null, HttpStatusCode.OK);

        cashflow.GetProperty("unlinkedLoanTransactions").GetProperty("count").GetInt32().Should().Be(1);
        spendable.GetProperty("unlinkedLoanTransactions").GetProperty("count").GetInt32().Should().Be(1);
        cashflow.GetProperty("startBalanceCents").GetInt64().Should().Be(before.GetProperty("startBalanceCents").GetInt64() + 700_000, "chỉ số dư thật đổi; cảnh báo không đổi số");
    }

    [Fact]
    public async Task ASingleWalletForecast_LeavesLoansOut()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync(balance: 10_000_000);
        var today = VietnamTime.Today();
        await DebtAsync(api, "Chị Hoa", "borrow", 3_000_000, today.AddDays(3));

        var cashflow = await api.ExpectAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?accountId={vcb}&to={today.AddDays(10):yyyy-MM-dd}", null, HttpStatusCode.OK);

        cashflow.GetProperty("days").EnumerateArray().SelectMany(d => d.GetProperty("events").EnumerateArray())
            .Should().NotContain(e => e.GetProperty("kind").GetString() == "loan_outflow", "nợ không thuộc ví nào");
    }
}
