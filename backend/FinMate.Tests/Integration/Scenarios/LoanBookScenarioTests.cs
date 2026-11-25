using System.Net;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Kịch bản đầu-cuối bắt buộc của Phase 90 (sổ vay hợp nhất vào <c>/loans</c>), qua đúng các endpoint app gọi: (a)–(f) của Phase 86 cũ, (g) khoản tạo như
/// app đang làm rồi trả qua thông báo, (h) trả có ví rồi thông báo cùng số về (S4), (i) trả kèm lãi (S1). Đồng hồ là giờ thật: "quá hạn" được dựng
/// bằng cách lùi hạn trong DB, job nhắc chạy với giờ truyền vào.
/// </summary>
[Collection("Integration")]
public class LoanBookScenarioTests : IClassFixture<RefundApiFactory>
{
    private const string VcbPackage = "com.VCB";
    private readonly RefundApiFactory _factory;
    private readonly HttpClient _client;

    public LoanBookScenarioTests(RefundApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(LoanTestApi Api, Guid Vcb)> StartAsync(long vcbBalance = 10_000_000)
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var provider = await api.InDbAsync(db => db.ProviderConfigs.Where(p => p.ProviderKey == "vietcombank").Select(p => p.Id).SingleAsync());
        var vcb = (await api.ExpectAsync(HttpMethod.Post, "/api/v1/financial-accounts", new CreateFinancialAccountRequest("VCB", AccountType.Bank, provider, vcbBalance), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        return (api, vcb);
    }

    private static async Task<Guid> NotifyVcbAsync(LoanTestApi api, long amount, string text, string? hint, string? strength, string type = "credit")
    {
        var at = DateTimeOffset.UtcNow;
        FakeAIServiceClient.SetScenario(VcbPackage, new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(amount, type, text, text, at, null, 0.95, hint, null, DebtHintStrength: strength),
            new CategorizationResult(type == "credit" ? "income" : "other", 0.95),
            new DuplicateResult(false, null),
            new ModelVersions("1", "1", "1"),
            10));
        var sign = type == "credit" ? "+" : "-";
        var analysis = await api.ExpectAsync(HttpMethod.Post, "/api/v1/notifications/analyze", new
        {
            packageName = VcbPackage, notificationTitle = "VCB", notificationBody = $"TK 0011: {sign}{amount}VND {text} {Guid.NewGuid():N}", receivedAt = at,
        }, HttpStatusCode.OK);
        return analysis.GetProperty("draftTransactionId").GetGuid();
    }

    private static async Task<Guid> LoanAsync(LoanTestApi api, object body)
        => (await api.ExpectAsync(HttpMethod.Post, "/api/v1/loans", body, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<JsonElement> LoanDetailAsync(LoanTestApi api, Guid id) => (await api.ExpectAsync(HttpMethod.Get, $"/api/v1/loans/{id}", null, HttpStatusCode.OK));

    private static long Income(JsonElement summary) => summary.GetProperty("totalIncomeCents").GetInt64();

    private static long Spent(JsonElement summary) => summary.GetProperty("totalSpentCents").GetInt64();

    private static long Flow(JsonElement summary, string field) => summary.GetProperty("cashFlow").GetProperty(field).GetInt64();

    [Fact]
    public async Task A_LendToNam_BankSaysNamRepaid_LinkFromTheSuggestion_ThenSettle()
    {
        var (api, vcb) = await StartAsync();
        var spentBefore = Spent(await api.MonthlySummaryAsync());

        var nam = await LoanAsync(api, new
        {
            direction = "lend", title = "Cho Nam vay", counterparty = "Nam", principalCents = 2_000_000, createTransaction = new { financialAccountId = vcb },
        });
        (await api.BalanceAsync(vcb)).Should().Be(8_000_000);
        Spent(await api.MonthlySummaryAsync()).Should().Be(spentBefore, "cho mượn không phải chi tiêu");

        var draft = await NotifyVcbAsync(api, 500_000, "NAM TRA NO", "debt_repayment", "strong");
        var detail = await api.TransactionDetailAsync(draft);
        detail.GetProperty("loanSuggestions")[0].GetProperty("loanId").GetGuid().Should().Be(nam);
        _factory.Push.Sent.Where(p => p.UserId == api.UserId).Select(p => p.Action).Should().Contain("choose_loan_link");

        var confirmed = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = nam }, HttpStatusCode.OK);
        confirmed.GetProperty("loanLink").GetProperty("loanId").GetGuid().Should().Be(nam);
        (await LoanDetailAsync(api, nam)).GetProperty("loan").GetProperty("remainingCents").GetInt64().Should().Be(1_500_000);

        var settled = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{nam}/repayments", new { amountCents = 1_500_000, financialAccountId = vcb }, HttpStatusCode.OK);
        settled.GetProperty("status").GetString().Should().Be("settled");
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000);
    }

    /// <summary>(b) + Q16: gắn khoản thu đã xác nhận vào khoản vay thì thu nhập tháng giảm, loanInflowCents tăng đúng bằng số đó, netCents không đổi.</summary>
    [Fact]
    public async Task B_RepaymentConfirmedAsIncome_ThenLinked_MovesToLoan_IncomeDrops_CashFlowHolds()
    {
        var (api, _) = await StartAsync();
        var nam = await LoanAsync(api, new { direction = "lend", title = "Cho Nam vay", counterparty = "Nam", principalCents = 2_000_000 });
        var incomeBefore = Income(await api.MonthlySummaryAsync());

        var draft = await NotifyVcbAsync(api, 500_000, "CK TRA NO", "debt_repayment", "strong");
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { categoryId = await api.CategoryAsync("income") }, HttpStatusCode.OK);
        var asIncome = await api.MonthlySummaryAsync();
        Income(asIncome).Should().Be(incomeBefore + 500_000, "AI xếp vào Thu nhập và đã xác nhận");

        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{nam}/repayments", new { transactionId = draft }, HttpStatusCode.OK);

        (await api.TransactionDetailAsync(draft)).GetProperty("categoryId").GetGuid().Should().Be(await api.CategoryAsync("loan"));
        var linked = await api.MonthlySummaryAsync();
        Income(linked).Should().Be(incomeBefore, "tổng thu tháng giảm 500k");
        (Flow(linked, "loanInflowCents") - Flow(asIncome, "loanInflowCents")).Should().Be(500_000, "Q16: dòng tiền vay tăng đúng bằng số đó");
        Flow(linked, "netCents").Should().Be(Flow(asIncome, "netCents"), "Q16: số dư thay đổi giữ nguyên");
    }

    [Fact]
    public async Task C_BorrowFromHoa_CashflowLeavesAllThreeLinesOnTheDueDate_AndSpendableTakesItOut()
    {
        var (api, vcb) = await StartAsync();
        var today = VietnamTime.Today();
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var due = today.AddDays(Math.Max(1, Math.Min(10, monthEnd.DayNumber - today.DayNumber)));
        var spendableBefore = (await api.ExpectAsync(HttpMethod.Get, "/api/v1/forecast/spendable", null, HttpStatusCode.OK)).GetProperty("spendableCents").GetInt64();

        await LoanAsync(api, new
        {
            direction = "borrow", title = "Vay chị Hoa", counterparty = "Chị Hoa", principalCents = 3_000_000, dueDate = due, createTransaction = new { financialAccountId = vcb },
        });

        var cashflow = await api.ExpectAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={due.AddDays(2):yyyy-MM-dd}", null, HttpStatusCode.OK);
        var days = cashflow.GetProperty("days").EnumerateArray().ToList();
        var on = days.Single(d => d.GetProperty("date").GetString() == due.ToString("yyyy-MM-dd"));
        var before = days.Single(d => d.GetProperty("date").GetString() == due.AddDays(-1).ToString("yyyy-MM-dd"));
        on.GetProperty("events").EnumerateArray().Should().ContainSingle(e => e.GetProperty("kind").GetString() == "loan_outflow" && e.GetProperty("amountCents").GetInt64() == -3_000_000);
        on.GetProperty("flexibleSpendCents").GetInt64().Should().Be(0, "khoản vay là loan, không thành thói quen chi");
        foreach (var line in new[] { "expectedBalanceCents", "lowBalanceCents", "highBalanceCents" })
        {
            (before.GetProperty(line).GetInt64() - on.GetProperty(line).GetInt64()).Should().Be(3_000_000, line);
        }

        var spendable = await api.ExpectAsync(HttpMethod.Get, "/api/v1/forecast/spendable", null, HttpStatusCode.OK);
        spendable.GetProperty("loansDueCents").GetInt64().Should().Be(3_000_000);
        spendable.GetProperty("spendableCents").GetInt64().Should().Be(spendableBefore, "3tr vay vào ví rồi bị trừ lại đúng khoản phải trả");
    }

    [Fact]
    public async Task D_LanOwesMe_OnlyTheOptimisticLine_ThenOverdue_DropsOut_AndIsReminded()
    {
        var (api, _) = await StartAsync();
        var today = VietnamTime.Today();
        var lan = await LoanAsync(api, new
        {
            direction = "lend", title = "Cho Lan vay", counterparty = "Lan", principalCents = 1_000_000, dueDate = today.AddDays(5), startDate = today.AddDays(-30),
        });

        var cashflow = await api.ExpectAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={today.AddDays(10):yyyy-MM-dd}", null, HttpStatusCode.OK);
        var inflow = cashflow.GetProperty("days").EnumerateArray().SelectMany(d => d.GetProperty("events").EnumerateArray())
            .Single(e => e.GetProperty("kind").GetString() == "loan_inflow");
        inflow.GetProperty("optimisticOnly").GetBoolean().Should().BeTrue();
        (cashflow.GetProperty("endHighBalanceCents").GetInt64() - cashflow.GetProperty("endBalanceCents").GetInt64()).Should().Be(1_000_000, "chỉ đường lạc quan có tiền vào");

        // Qua hạn mà chưa trả: dựng bằng cách lùi hạn về hôm qua.
        await api.InDbAsync(db => db.Loans.Where(l => l.Id == lan).ExecuteUpdateAsync(s => s.SetProperty(l => l.DueDate, today.AddDays(-1))));
        var overdue = await api.ExpectAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?to={today.AddDays(10):yyyy-MM-dd}", null, HttpStatusCode.OK);
        overdue.GetProperty("days").EnumerateArray().SelectMany(d => d.GetProperty("events").EnumerateArray())
            .Should().NotContain(e => e.GetProperty("kind").GetString() == "loan_inflow");

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<LoanReminderJob>().RunAtAsync(DateTimeOffset.UtcNow);
        }

        var alerts = await api.InDbAsync(db => db.ForecastAlerts.Where(a => a.UserId == api.UserId && a.Kind == ForecastAlertKind.LoanDue).ToListAsync());
        alerts.Should().ContainSingle(a => a.DedupKey.EndsWith(":overdue:0") && a.Body == "Bạn có một khoản nợ đã quá hạn");
    }

    [Fact]
    public async Task E_PayingMoreThanLeft_Is422_AndDeletingTheLastPaymentOfASettledLoan_ReopensIt()
    {
        var (api, _) = await StartAsync();
        var id = await LoanAsync(api, new { direction = "lend", title = "Cho Nam vay", counterparty = "Nam", principalCents = 1_000_000 });
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 500_000 }, HttpStatusCode.OK);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 600_000 }, HttpStatusCode.UnprocessableEntity))
            .Should().Be("LOAN_REPAYMENT_EXCEEDS_REMAINING");

        var settled = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 500_000 }, HttpStatusCode.OK);
        settled.GetProperty("status").GetString().Should().Be("settled");
        var last = (await LoanDetailAsync(api, id)).GetProperty("entries").EnumerateArray().Last(e => e.GetProperty("kind").GetString() == "repayment").GetProperty("id").GetGuid();

        await api.ExpectAsync(HttpMethod.Delete, $"/api/v1/loans/{id}/entries/{last}", null, HttpStatusCode.OK);

        var reopened = (await LoanDetailAsync(api, id)).GetProperty("loan");
        reopened.GetProperty("status").GetString().Should().Be("open");
        reopened.GetProperty("remainingCents").GetInt64().Should().Be(500_000);
    }

    [Fact]
    public async Task F_AnOldLoanWithoutATransaction_AndACashPayment_LeaveBalancesAlone()
    {
        var (api, vcb) = await StartAsync();
        var cash = await api.WalletAsync("Tiền mặt", 2_000_000);

        var id = await LoanAsync(api, new
        {
            direction = "borrow", title = "Vay bạn cũ", counterparty = "Bạn cũ", principalCents = 5_000_000, startDate = VietnamTime.Today().AddYears(-1),
        });
        var paid = await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 1_000_000 }, HttpStatusCode.OK);

        paid.GetProperty("remainingCents").GetInt64().Should().Be(4_000_000);
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000);
        (await api.BalanceAsync(cash)).Should().Be(2_000_000);
    }

    /// <summary>(g): khoản tạo đúng như app đang gọi (POST /loans không ví), rồi ngân hàng báo người đó trả; gợi ý có tên, xác nhận kèm loanId.</summary>
    [Fact]
    public async Task G_ALoanCreatedLikeTheAppDoes_RepaidThroughABankNotificationWithASuggestion()
    {
        var (api, vcb) = await StartAsync();
        var id = await LoanAsync(api, new { direction = "lend", title = "Cho bạn An vay", counterparty = "Bạn An", principalCents = 700_000, interestRatePercentYear = 0 });

        var draft = await NotifyVcbAsync(api, 700_000, "NGUYEN VAN AN CHUYEN TIEN", null, null);
        var suggestion = (await api.TransactionDetailAsync(draft)).GetProperty("loanSuggestions")[0];
        (suggestion.GetProperty("loanId").GetGuid(), suggestion.GetProperty("nameMatched").GetBoolean()).Should().Be((id, true));

        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = id }, HttpStatusCode.OK);

        var loans = await api.ExpectAsync(HttpMethod.Get, "/api/v1/loans", null, HttpStatusCode.OK);
        var loan = loans.EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);
        (loan.GetProperty("paidCents").GetInt64(), loan.GetProperty("remainingCents").GetInt64(), loan.GetProperty("status").GetString())
            .Should().Be((700_000L, 0L, "settled"));
        (await api.BalanceAsync(vcb)).Should().Be(10_700_000);
    }

    /// <summary>(h), S4: trả có ví, rồi thông báo ngân hàng cùng số về → nghi trùng → gộp; liên kết ở bản giữ lại, paidCents và số dư ví đúng.</summary>
    [Fact]
    public async Task H_ARepaymentThroughAWallet_ThenTheBankNotification_IsSuspected_AndMerges()
    {
        var (api, vcb) = await StartAsync();
        var id = await LoanAsync(api, new { direction = "borrow", title = "Vay Nam", counterparty = "Nam", principalCents = 2_000_000 });
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 500_000, financialAccountId = vcb }, HttpStatusCode.OK);
        var manual = (await LoanDetailAsync(api, id)).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "repayment").GetProperty("transactionId").GetGuid();

        var notice = await NotifyVcbAsync(api, 500_000, "TRA NO NAM", null, null, "debit");
        var draft = await api.TransactionDetailAsync(notice);
        draft.GetProperty("duplicateStatus").GetString().Should().Be("Suspected");

        var survivor = (await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{notice}/merge", new { withTransactionId = manual }, HttpStatusCode.OK))
            .GetProperty("id").GetGuid();

        var detail = await LoanDetailAsync(api, id);
        detail.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("kind").GetString() == "repayment").GetProperty("transactionId").GetGuid().Should().Be(survivor);
        detail.GetProperty("loan").GetProperty("paidCents").GetInt64().Should().Be(500_000);
        (await api.BalanceAsync(vcb)).Should().Be(9_500_000, "không trừ hai lần");
    }

    /// <summary>(i), S1: 2tr lãi 12%/năm, sau 90 ngày, thông báo −2.060.000 → khoản settled, giao dịch lãi 60.000 tính vào chi tiêu tháng.</summary>
    [Fact]
    public async Task I_ABankNotificationOf2060000_SettlesA12PercentLoanAfter90Days_And60000IsSpending()
    {
        var (api, vcb) = await StartAsync();
        var today = VietnamTime.Today();
        var id = await LoanAsync(api, new
        {
            direction = "borrow", title = "Vay Nam", counterparty = "Nam", principalCents = 2_000_000, interestRatePercentYear = 12, startDate = today.AddDays(-90),
        });
        var spentBefore = Spent(await api.MonthlySummaryAsync());

        var draft = await NotifyVcbAsync(api, 2_060_000, "TRA NO NAM", "debt_repayment", "strong", "debit");
        (await api.TransactionDetailAsync(draft)).GetProperty("loanSuggestions")[0].GetProperty("interestCents").GetInt64().Should().Be(60_000);
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", new { loanId = id }, HttpStatusCode.OK);

        (await LoanDetailAsync(api, id)).GetProperty("loan").GetProperty("status").GetString().Should().Be("settled");
        (await api.BalanceAsync(vcb)).Should().Be(10_000_000 - 2_060_000);
        var month = await api.MonthlySummaryAsync();
        Spent(month).Should().Be(spentBefore + 60_000, "lãi vay là chi tiêu thật");
        Flow(month, "loanOutflowCents").Should().Be(2_000_000);
    }
}
