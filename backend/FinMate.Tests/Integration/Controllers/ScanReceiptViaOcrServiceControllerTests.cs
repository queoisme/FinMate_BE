using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// <c>POST /transactions/scan-receipt</c> khi OCR service được cấu hình: tầng HTTP thật của backend, OCR service và AI Service giả.
/// Chứng minh backend điều phối đúng thứ tự (OCR rồi phân loại) và không còn đi qua <c>POST /api/v1/ocr</c>.
/// </summary>
[Collection("Integration")]
public class ScanReceiptViaOcrServiceControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;

    public ScanReceiptViaOcrServiceControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private sealed class FakeOcrServiceClient(Func<OcrServiceResponse> respond) : IOcrServiceClient
    {
        public int Calls { get; private set; }
        public bool IsConfigured => true;

        public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private sealed class DownOcrServiceClient : IOcrServiceClient
    {
        public bool IsConfigured => true;

        public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
            => throw new AIServiceUnavailableException("OCR service down (fake).");
    }

    private HttpClient ClientWith(IOcrServiceClient ocr)
        => _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOcrServiceClient>();
            services.AddSingleton(ocr);
        })).CreateClient();

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Scan Test User"));
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage ScanRequest(string token)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", "hoa-don.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions/scan-receipt") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static OcrServiceResponse Highlands() => new(
        true, 16_000, "Qwerty Cafe Scan Test", "Qwerty Cafe", ["Qwerty Cafe Scan Test", "Tong tien: 16.000"],
        "dining", "brand", false, new DateTimeOffset(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7)), "text");

    [Fact]
    public async Task TheBackendOrchestratesOcrThenClassification()
    {
        FakeAIServiceClient.SetBill("Qwerty Cafe Scan Test", new BillClassifyResponse("food", "dining", 0.8, "brand"));
        var ocr = new FakeOcrServiceClient(Highlands);
        var client = ClientWith(ocr);
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(ScanRequest(token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("ocrResult").GetString().Should().Be("success");
        data.GetProperty("amountCents").GetInt64().Should().Be(16_000);
        data.GetProperty("merchantName").GetString().Should().Be("Qwerty Cafe");
        data.GetProperty("categorySlug").GetString().Should().Be("food");
        data.GetProperty("confidence").GetDouble().Should().BeLessThan(0.85);
        ocr.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ScanningCreatesNoTransaction()
    {
        var client = ClientWith(new FakeOcrServiceClient(Highlands));
        var token = await RegisterAndLoginAsync(client);

        await client.SendAsync(ScanRequest(token));

        var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/transactions");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(list);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task OcrServiceDownIsA503WithTheAiUnavailableCode()
    {
        var client = ClientWith(new DownOcrServiceClient());
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(ScanRequest(token));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AnUnreadableImageIsA200WithTheUnreadableResult()
    {
        var client = ClientWith(new FakeOcrServiceClient(() => new OcrServiceResponse(false, null, null, null, [], null, null, false, null, null)));
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(ScanRequest(token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("ocrResult").GetString().Should().Be("unreadable");
    }

    // ------------------------------------------------------------ bản ghi bóc tách (Postgres thật)

    private async Task<List<ReceiptScan>> ScansOfMerchantAsync(string merchant)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.ReceiptScans.AsNoTracking().Where(s => s.MerchantName == merchant).ToListAsync();
    }

    [Fact]
    public async Task EachScanIsRecordedWithTheExtractedFieldsAndItsIdIsReturned()
    {
        const string merchant = "Record Cafe Scan Test";
        FakeAIServiceClient.SetBill(merchant, new BillClassifyResponse("food", "dining", 0.8, "brand"));
        var client = ClientWith(new FakeOcrServiceClient(() => Highlands() with { MerchantName = merchant, MerchantDisplay = merchant }));
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(ScanRequest(token));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var scanId = doc.RootElement.GetProperty("data").GetProperty("scanId").GetGuid();
        var scan = (await ScansOfMerchantAsync(merchant)).Should().ContainSingle().Subject;
        scan.Id.Should().Be(scanId);
        scan.OcrResult.Should().Be("success");
        scan.AmountCents.Should().Be(16_000);
        scan.TransactedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7)));
        scan.CategorySlug.Should().Be("food");
        scan.CategorySource.Should().Be("brand");
        scan.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task TheTableHoldsNoColumnForImageOrText()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();

        var columns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'receipt_scans'")
            .ToListAsync();

        columns.Should().BeEquivalentTo(
            "id", "user_id", "ocr_result", "amount_cents", "merchant_name", "transacted_at", "category_slug", "category_source",
            "confidence", "needs_review", "created_at");

        // Bảng món cũng không có cột chữ OCR.
        var itemColumns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'receipt_scan_items'")
            .ToListAsync();

        itemColumns.Should().BeEquivalentTo(
            "id", "scan_id", "position", "name", "amount_cents", "category_slug", "category_source", "parent_position");
    }

    [Fact]
    public async Task TheDatabaseRejectsAnImpossibleScan()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var userId = await db.Users.IgnoreQueryFilters().Select(u => u.Id).FirstAsync();

        db.ReceiptScans.Add(new ReceiptScan { UserId = userId, OcrResult = "maybe", CreatedAt = DateTimeOffset.UtcNow });

        await ((Func<Task>)(() => db.SaveChangesAsync())).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingTheUserDeletesTheirScans()
    {
        const string merchant = "Cascade Cafe Scan Test";
        var client = ClientWith(new FakeOcrServiceClient(() => Highlands() with { MerchantName = merchant, MerchantDisplay = merchant }));
        var token = await RegisterAndLoginAsync(client);
        await client.SendAsync(ScanRequest(token));
        var scan = (await ScansOfMerchantAsync(merchant)).Should().ContainSingle().Subject;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == scan.UserId).ExecuteDeleteAsync();
        }

        (await ScansOfMerchantAsync(merchant)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheOldRouteRecordsScansToo()
    {
        // OCR service chưa cấu hình: quét qua POST /api/v1/ocr của AI Service (AI giả trả WINMART+ NGUYEN TRAI 110.000đ).
        var client = _factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(ScanRequest(token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var scan = (await ScansOfMerchantAsync("WINMART+ NGUYEN TRAI")).OrderByDescending(s => s.CreatedAt).First();
        scan.AmountCents.Should().Be(110_000);
        scan.CategorySlug.Should().Be("shopping");
        scan.CategorySource.Should().BeNull();
    }
}
