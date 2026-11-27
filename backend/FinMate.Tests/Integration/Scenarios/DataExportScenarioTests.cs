using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Tests.Integration.Controllers;
using System.IO.Compression;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>Kịch bản đầu-cuối bắt buộc của Phase 91 (xuất dữ liệu), qua đúng các endpoint app gọi. (a), (b): CSV giao dịch; (c)–(e): gói dữ liệu đầy đủ.</summary>
[Collection("Integration")]
public class DataExportScenarioTests : IClassFixture<DataExportApiFactory>
{
    private const string CsvUrl = "/api/v1/exports/transactions.csv";
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportScenarioTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private static async Task<byte[]> CsvAsync(LoanTestApi api, string query = "")
    {
        var response = await api.SendAsync(HttpMethod.Get, CsvUrl + query);
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
    /// "Số tiền tính vào chi tiêu" bằng <c>totalSpentCents</c> của <c>monthly-summary</c> cùng kỳ (D5). Sau đó xoá 2 khoản chi: mặc định CSV không có
    /// dòng đã xoá; <c>includeDeleted=true</c> có đủ 50 dòng, cột "Đã xoá lúc", dòng đã xoá tính 0 và tổng cột vẫn bằng <c>totalSpentCents</c> mới.
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
        var plainIds = new List<Guid>();
        for (var i = 0; i < 42; i++)
        {
            var amount = 10_000L * (i + 1);
            plain += amount;
            plainIds.Add(await api.TransactionAsync(wallets[i % 3], (i % 3) switch { 0 => food, 1 => transport, _ => null }, amount, merchant: $"Quán {i}"));
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
        rows[0].Should().NotContainKey("Đã xoá lúc");

        // Xoá 2 khoản chi thường (10.000 và 420.000).
        Guid[] deleted = [plainIds[0], plainIds[41]];
        foreach (var id in deleted)
        {
            (await api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var spentAfter = (await api.MonthlySummaryAsync()).GetProperty("totalSpentCents").GetInt64();
        spentAfter.Should().Be(plain + 300_000 - 10_000 - 420_000);

        var live = CsvTestReader.Read(await CsvAsync(api));
        live.Should().HaveCount(48, "mặc định không gồm giao dịch đã xoá");
        live.Select(r => r["Mã giao dịch"]).Should().NotContain(deleted.Select(d => d.ToString()));

        var all = CsvTestReader.Read(await CsvAsync(api, "?includeDeleted=true"));
        all.Should().HaveCount(50);
        var gone = all.Where(r => r["Đã xoá lúc"] != string.Empty).ToList();
        gone.Select(r => r["Mã giao dịch"]).Should().BeEquivalentTo(deleted.Select(d => d.ToString()));
        gone.Should().OnlyContain(r => r["Số tiền tính vào chi tiêu"] == "0" && r["Tính vào chi tiêu"] == "Không" && r["Đã xoá lúc"].EndsWith("+07:00"));
        all.Sum(r => long.Parse(r["Số tiền tính vào chi tiêu"])).Should().Be(spentAfter, "dòng đã xoá tính 0 nên tổng cột 17 vẫn bằng totalSpentCents");
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

    private static async Task<Dictionary<string, string>> DownloadAsync(LoanTestApi api, Guid id)
    {
        var url = (await api.ExpectAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.OK)).GetProperty("url").GetString()!;
        var response = await api.SendAsync(HttpMethod.Get, url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        return zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open(), Encoding.UTF8).ReadToEnd());
    }

    /// <summary>(c) Không mã → 403; có mã → Pending → chạy job → Ready → lấy link → tải được ZIP → gọi lại link → 410.</summary>
    [Fact]
    public async Task C_WithoutAnOtp403_WithOne_PendingReadyDownloadOnce()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var wallet = await api.WalletAsync();
        await api.TransactionAsync(wallet, null, 50_000, merchant: "Cơm tấm");

        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { }, HttpStatusCode.Forbidden)).Should().Be(DataExportErrorCodes.OtpInvalid);
        var id = await api.RequestExportAsync(_factory);
        (await api.ExportAsync(id)).GetProperty("status").GetString().Should().Be("Pending");

        await _factory.RunJobAsync(id);
        (await api.ExportAsync(id)).GetProperty("status").GetString().Should().Be("Ready");

        var url = (await api.ExpectAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.OK)).GetProperty("url").GetString()!;
        var download = await api.SendAsync(HttpMethod.Get, url);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var zip = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync())))
        {
            zip.GetEntry("README.txt").Should().NotBeNull();
            new StreamReader(zip.GetEntry("transactions.csv")!.Open()).ReadToEnd().Should().Contain("Cơm tấm");
        }

        (await api.SendAsync(HttpMethod.Get, url)).StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    /// <summary>(d) A và B cùng nhóm; B chia sẻ giao dịch vào nhóm → gói của A không có giao dịch, tên hay email của B.</summary>
    [Fact]
    public async Task D_AnotherMembersSharedTransaction_NameAndEmail_AreNotInMyPackage()
    {
        var a = await new LoanTestApi(_factory, _client).SignUpAsync();
        var b = await new LoanTestApi(_factory, _client).SignUpAsync();
        var bTx = await b.TransactionAsync(await b.WalletAsync("Ví của B"), null, 654_321, merchant: "Quán nhậu của B");
        var bInfo = await b.InDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var user = await db.Users.SingleAsync(u => u.Id == b.UserId);
            user.DisplayName = "Nguyễn Văn Bê";
            var group = new Group { Id = Guid.NewGuid(), Name = "Nhà chung", CreatedByUserId = b.UserId, CreatedAt = now, UpdatedAt = now };
            db.Add(group);
            db.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = b.UserId, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
            db.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = a.UserId, Role = GroupRole.Member, JoinedAt = now, UpdatedAt = now });
            var shared = await db.Transactions.SingleAsync(t => t.Id == bTx);
            (shared.GroupId, shared.SharedAt, shared.Description) = (group.Id, now, "Ghi chú của B");
            await db.SaveChangesAsync();
            return (user.Email, user.DisplayName);
        });

        var id = await a.RequestExportAsync(_factory);
        await _factory.RunJobAsync(id);
        var files = await DownloadAsync(a, id);
        var everything = string.Join("\n", files.Values);

        new[] { bTx.ToString(), "Quán nhậu của B", "Ghi chú của B", "654321", "Ví của B", bInfo.Email, bInfo.DisplayName, b.UserId.ToString() }
            .Where(s => everything.Contains(s, StringComparison.Ordinal)).Should().BeEmpty();
        files["groups.json"].Should().Contain("Nhà chung").And.Contain("\"isYou\": true");
    }

    /// <summary>(e) Yêu cầu lần hai trong 24 giờ → 429. Sau <c>fileRetentionHours</c> → <c>Expired</c>, file đã xoá.</summary>
    [Fact]
    public async Task E_ASecondRequestWithin24Hours_Is429_AndAfterRetentionTheFileIsGone()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);
        await _factory.RunJobAsync(id);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/otp", null, HttpStatusCode.TooManyRequests)).Should().Be(DataExportErrorCodes.RateLimited);
        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { code = "123456" }, HttpStatusCode.TooManyRequests)).Should().Be(DataExportErrorCodes.RateLimited);

        try
        {
            _factory.Clock.Shift = TimeSpan.FromHours(73);
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<FinMate.Application.DataExport.PersonalDataExportCleanup>().RunAsync();
        }
        finally
        {
            _factory.Clock.Shift = TimeSpan.Zero;
        }

        (await api.ExportAsync(id)).GetProperty("status").GetString().Should().Be("Expired");
        (await api.InDbAsync(db => db.PersonalDataExportArchives.CountAsync(a => a.ExportId == id))).Should().Be(0);
    }
}
