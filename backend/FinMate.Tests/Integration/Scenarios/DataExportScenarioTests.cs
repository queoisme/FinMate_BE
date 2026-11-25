using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>Kịch bản đầu-cuối bắt buộc của Phase 91 (xuất dữ liệu), qua đúng các endpoint app gọi. (a), (b): CSV giao dịch; (c)–(e): gói dữ liệu đầy đủ.</summary>
[Collection("Integration")]
public class DataExportScenarioTests : IClassFixture<AuthApiFactory>
{
    private const string CsvUrl = "/api/v1/exports/transactions.csv";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportScenarioTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static async Task<byte[]> CsvAsync(LoanTestApi api)
    {
        var response = await api.SendAsync(HttpMethod.Get, CsvUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<Guid> PostIdAsync(LoanTestApi api, string url, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var data = await api.ExpectAsync(HttpMethod.Post, url, body, expected);
        return data.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// (a) 3 ví, 50 giao dịch tháng này gồm hoàn tiền, cặp đảo, chuyển ví, cho vay và được trả nợ. Đọc CSV bằng trình đọc thật: đúng 50 dòng, và tổng cột
    /// "Số tiền tính vào chi tiêu" bằng <c>totalSpentCents</c> của <c>monthly-summary</c> cùng kỳ (D5).
    /// </summary>
    [Fact]
    public async Task A_FiftyTransactionsAcrossThreeWallets_SumOfTheSpendingColumn_MatchesTheMonthlySummary()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        Guid[] wallets = [await api.WalletAsync("VCB", 50_000_000), await api.WalletAsync("MoMo", 50_000_000), await api.WalletAsync("Tiền mặt", 50_000_000)];
        var (food, transport, income) = (await api.CategoryAsync("food"), await api.CategoryAsync("transport"), await api.CategoryAsync("income"));

        // Cho vay 1.000.000 từ ví (1 giao dịch) và được trả 400.000 (1 giao dịch): không phải chi tiêu (Q16).
        var loan = await PostIdAsync(api, "/api/v1/loans", new
        {
            direction = "lend", title = "Cho An mượn", counterparty = "An", principalCents = 1_000_000L, createTransaction = new { financialAccountId = wallets[0] },
        });
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{loan}/repayments", new { amountCents = 400_000L, financialAccountId = wallets[0] }, HttpStatusCode.OK);

        // Chuyển ví 2.000.000 (1 giao dịch).
        await PostIdAsync(api, "/api/v1/transactions/transfer", new
        {
            fromAccountId = wallets[0], toAccountId = wallets[1], amountCents = 2_000_000L, transactedAt = DateTimeOffset.UtcNow, description = "nạp momo",
        });

        // Mua 500.000, được hoàn 200.000 (2 giao dịch): chi ròng 300.000.
        var bought = await api.TransactionAsync(wallets[1], food, 500_000, merchant: "Shopee");
        await PostIdAsync(api, $"/api/v1/transactions/{bought}/refunds", new { amountCents = 200_000L });

        // Cặp đảo 300.000 (2 giao dịch): bị loại khỏi mọi tổng.
        var wrong = await api.TransactionAsync(wallets[2], food, 300_000, merchant: "Trừ nhầm");
        var undo = await api.TransactionAsync(wallets[2], null, 300_000, "Credit", merchant: "Hoàn trừ nhầm");
        await api.ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{undo}/related", new { relatedTransactionId = wrong, relation = "Reversal" }, HttpStatusCode.OK);

        // Lương (1 giao dịch) và 42 khoản chi thường.
        await api.TransactionAsync(wallets[0], income, 15_000_000, "Credit", merchant: "Lương");
        long plain = 0;
        for (var i = 0; i < 42; i++)
        {
            var amount = 10_000L * (i + 1);
            plain += amount;
            await api.TransactionAsync(wallets[i % 3], (i % 3) switch { 0 => food, 1 => transport, _ => null }, amount, merchant: $"Quán {i}");
        }

        var rows = CsvTestReader.Read(await CsvAsync(api));
        var summary = await api.MonthlySummaryAsync();

        rows.Should().HaveCount(50);
        var spentColumn = rows.Sum(r => long.Parse(r["Số tiền tính vào chi tiêu"]));
        spentColumn.Should().Be(summary.GetProperty("totalSpentCents").GetInt64());
        spentColumn.Should().Be(plain + 300_000, "chi thường + (500.000 − 200.000 hoàn); vay nợ, chuyển ví, cặp đảo, lương là 0");
        rows.Count(r => r["Quan hệ"] == "Vay nợ").Should().Be(2);
        rows.Single(r => r["Loại"] == "Chuyển ví")["Ví đích"].Should().Be("MoMo");
        rows.Single(r => r["Quan hệ"] == "Hoàn tiền")["Liên quan đến"].Should().Be(bought.ToString());
        rows.Where(r => r["Quan hệ"] is "Đảo giao dịch" or "Đã bị đảo").Should().HaveCount(2).And.OnlyContain(r => r["Tính vào chi tiêu"] == "Không");
    }

    /// <summary>(b) Ghi chú là công thức: trong CSV thành <c>'=HYPERLINK("x")</c>, không chạy được trong bảng tính.</summary>
    [Fact]
    public async Task B_AFormulaInTheNote_IsNeutralisedWithAQuotePrefix()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var wallet = await api.WalletAsync();
        await api.ExpectAsync(HttpMethod.Post, "/api/v1/transactions", new
        {
            financialAccountId = wallet, amountCents = 10_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, description = "=HYPERLINK(\"x\")",
        }, HttpStatusCode.Created);

        var bytes = await CsvAsync(api);

        Encoding.UTF8.GetString(bytes).Should().Contain("\"'=HYPERLINK(\"\"x\"\")\"");
        CsvTestReader.Read(bytes).Single()["Ghi chú"].Should().Be("'=HYPERLINK(\"x\")");
    }
}
