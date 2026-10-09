using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Quét → lưu giao dịch với <c>receiptScanId</c> → bản ghi quét được nối với giao dịch, trên Postgres thật.
/// OCR service giả trả đúng một tên cửa hàng riêng cho từng test để đọc lại đúng bản ghi của mình.
/// </summary>
[Collection("Integration")]
public class ReceiptScanLinkControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ReceiptScanLinkControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOcrServiceClient>();
            services.AddSingleton<IOcrServiceClient, ScanByMerchantOcr>();
        })).CreateClient();
    }

    /// <summary>OCR giả: tên cửa hàng lấy từ tên file ảnh, nên mỗi test tự đặt tên riêng.</summary>
    private sealed class ScanByMerchantOcr : IOcrServiceClient
    {
        public bool IsConfigured => true;

        public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
            => Task.FromResult(new OcrServiceResponse(
                true, 16_000, request.FileName, request.FileName, [request.FileName], "dining", "brand", false, null, "text"));
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Link Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private async Task<Guid> ScanAsync(string token, string merchant)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", merchant);
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions/scan-receipt", token, form));
        return (await DataOf(response)).GetProperty("scanId").GetGuid();
    }

    private async Task<(Guid Account, Guid Category)> AccountAndCategoryAsync(string token, string slug)
    {
        var account = await DataOf(await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/financial-accounts", token, JsonContent.Create(new
        {
            accountName = "Ví", accountType = "Cash", providerConfigId = (Guid?)null, initialBalanceCents = 1_000_000,
        }))));
        var categories = await DataOf(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/categories", token)));
        var category = categories.EnumerateArray().First(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
        return (account.GetProperty("id").GetGuid(), category);
    }

    private async Task<HttpResponseMessage> SaveAsync(
        string token, Guid account, Guid category, string merchant, string source, Guid? scanId, Guid? clientRequestId = null)
        => await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions", token, JsonContent.Create(new
        {
            financialAccountId = account, categoryId = category, amountCents = 16_000, transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = merchant, description = (string?)null, source,
            receiptScanId = scanId, clientRequestId,
        })));

    private async Task<ReceiptScan> ScanRowAsync(Guid scanId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.ReceiptScans.AsNoTracking().SingleAsync(s => s.Id == scanId);
    }

    /// <summary>Các giao dịch đã nối với lần quét (bảng <c>receipt_scan_links</c>).</summary>
    private async Task<List<Guid>> LinkedTransactionsAsync(Guid scanId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.ReceiptScanLinks.AsNoTracking().Where(l => l.ScanId == scanId).Select(l => l.TransactionId).ToListAsync();
    }

    [Fact]
    public async Task SavingAScannedReceiptLinksTheScanToTheTransaction()
    {
        const string merchant = "Link Cafe Save Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var scanId = await ScanAsync(token, merchant);

        var response = await SaveAsync(token, account, category, merchant, "Receipt", scanId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var transactionId = (await DataOf(response)).GetProperty("id").GetGuid();
        (await LinkedTransactionsAsync(scanId)).Should().Equal(transactionId);
    }

    [Fact]
    public async Task TheLinkLetsUsCompareWhatTheAiGuessedWithWhatTheUserChose()
    {
        const string merchant = "Link Cafe Compare Test";
        var token = await RegisterAndLoginAsync();
        var (account, entertainment) = await AccountAndCategoryAsync(token, "entertainment");
        var scanId = await ScanAsync(token, merchant);
        await SaveAsync(token, account, entertainment, merchant, "Receipt", scanId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        // scan -> links -> transactions -> category
        var row = await db.ReceiptScanLinks.AsNoTracking()
            .Where(l => l.ScanId == scanId)
            .Select(l => new { Guessed = l.Scan!.CategorySlug, Chosen = l.Transaction!.Category!.Slug, l.Scan.AmountCents, Saved = l.Transaction!.AmountCents })
            .SingleAsync();

        row.Chosen.Should().Be("entertainment");
        row.Saved.Should().Be(row.AmountCents);  // số tiền AI đọc khớp số tiền người dùng lưu
        row.Guessed.Should().NotBe(row.Chosen);  // OCR giả không nhận ra cửa hàng này nên không đoán được slug
    }

    [Fact]
    public async Task AScanOfAnotherUserIsIgnoredAndTheSaveStillSucceeds()
    {
        const string merchant = "Link Cafe Foreign Test";
        var owner = await RegisterAndLoginAsync();
        var scanId = await ScanAsync(owner, merchant);
        var thief = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(thief, "food");

        var response = await SaveAsync(thief, account, category, merchant, "Receipt", scanId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await LinkedTransactionsAsync(scanId)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownScanIdIsIgnoredAndTheSaveStillSucceeds()
    {
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");

        var response = await SaveAsync(token, account, category, "Link Cafe Unknown Test", "Receipt", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OnlyReceiptSavesAreLinked()
    {
        const string merchant = "Link Cafe Manual Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var scanId = await ScanAsync(token, merchant);

        await SaveAsync(token, account, category, merchant, "Manual", scanId);

        (await LinkedTransactionsAsync(scanId)).Should().BeEmpty();
    }

    [Fact]
    public async Task AScanLinksToTheFirstTransactionOnly()
    {
        const string merchant = "Link Cafe Twice Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var scanId = await ScanAsync(token, merchant);

        var first = await DataOf(await SaveAsync(token, account, category, merchant, "Receipt", scanId));
        var second = await SaveAsync(token, account, category, merchant, "Receipt", scanId);  // hai lần Lưu cùng một lần quét

        second.StatusCode.Should().Be(HttpStatusCode.Created);  // vẫn lưu: đó là hai khoản chi thật
        (await LinkedTransactionsAsync(scanId)).Should().Equal(first.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ReplayingTheSameSaveKeepsTheOriginalLink()
    {
        const string merchant = "Link Cafe Replay Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var scanId = await ScanAsync(token, merchant);
        var clientRequestId = Guid.NewGuid();

        var first = await DataOf(await SaveAsync(token, account, category, merchant, "Receipt", scanId, clientRequestId));
        var replay = await SaveAsync(token, account, category, merchant, "Receipt", scanId, clientRequestId);

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LinkedTransactionsAsync(scanId)).Should().Equal(first.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task TheDatabaseNeverLinksTwoScansToOneTransaction()
    {
        const string merchant = "Link Cafe Unique Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var firstScan = await ScanAsync(token, merchant);
        var secondScan = await ScanAsync(token, merchant);
        var transactionId = (await DataOf(await SaveAsync(token, account, category, merchant, "Receipt", firstScan))).GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var act = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO receipt_scan_links (transaction_id, scan_id, group_category_slug) VALUES ({transactionId}, {secondScan}, 'food')");

        // SQL thô không đi qua change tracker nên lỗi ra thẳng dạng PostgresException; 23505 = vi phạm khóa chính transaction_id.
        (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task HardDeletingTheTransactionOnlyDetachesTheScan()
    {
        const string merchant = "Link Cafe Detach Test";
        var token = await RegisterAndLoginAsync();
        var (account, category) = await AccountAndCategoryAsync(token, "food");
        var scanId = await ScanAsync(token, merchant);
        var transactionId = (await DataOf(await SaveAsync(token, account, category, merchant, "Receipt", scanId))).GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Transactions.IgnoreQueryFilters().Where(t => t.Id == transactionId).ExecuteDeleteAsync();
        }

        var row = await ScanRowAsync(scanId);  // bản ghi bóc tách vẫn còn, chỉ mất dòng nối
        (await LinkedTransactionsAsync(scanId)).Should().BeEmpty();
        row.AmountCents.Should().Be(16_000);
    }
}
