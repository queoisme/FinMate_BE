using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Auth.Commands;
using FinMate.Domain.Entities;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Quét tách nhóm qua HTTP thật + Postgres thật: cờ <c>Receipt:SplitByItemsEnabled</c> đọc từ cấu hình,
/// cờ đồng ý chia sẻ đọc từ DB mỗi lần quét, các món lưu vào <c>receipt_scan_items</c>.
/// </summary>
[Collection("Integration")]
public class ScanReceiptSplitControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public ScanReceiptSplitControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        FakeAIServiceClient.ResetClassifyItems();
        FakeAIServiceClient.ClassifyItemsHandler = r => new ClassifyItemsResponse(
            r.Items.Select((item, i) => new ClassifiedItem(i, item.Name == "Dầu gội" ? "shopping" : "food", "rule")).ToList(), false);
    }

    private sealed class FakeOcrServiceClient(OcrServiceResponse response) : IOcrServiceClient
    {
        public bool IsConfigured => true;

        public Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default)
            => Task.FromResult(response);
    }

    private HttpClient ClientWith(OcrServiceResponse ocr, bool splitEnabled)
        => _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Receipt:SplitByItemsEnabled", splitEnabled ? "true" : "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOcrServiceClient>();
                services.AddSingleton<IOcrServiceClient>(new FakeOcrServiceClient(ocr));
            });
        }).CreateClient();

    // 0 Cà phê sữa 30.000 | 1 Trân châu 5.000 (topping) | 2 Giảm giá -5.000 | 3 VAT 8% 2.000 | 4 Dầu gội 50.000 = 82.000
    private static OcrServiceResponse Bill(string merchant, bool itemsOk = true) => new(
        true, 82_000, merchant, merchant, [merchant, "Tong tien: 82.000"],
        "shopping", "brand", false, new DateTimeOffset(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7)), "text",
        [
            new OcrItem("Cà phê sữa", 30_000, null, false),
            new OcrItem("Trân châu", 5_000, 0, false),
            new OcrItem("Giảm giá", -5_000, null, true),
            new OcrItem("VAT 8%", 2_000, null, false),
            new OcrItem("Dầu gội", 50_000, null, false),
        ],
        itemsOk);

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Split Test User"));
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ScanAsync(HttpClient client, string token)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", "hoa-don.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions/scan-receipt") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task SetConsentAsync(HttpClient client, string token, bool consent)
    {
        var request = consent
            ? new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/consents") { Content = JsonContent.Create(new AcceptConsentRequest(ConsentKind.Ai, ConsentVersions.Ai)) }
            : new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me/consents/ai");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static List<ClassifyItemsRequest> SeenFor(string merchant)
        => FakeAIServiceClient.ClassifyItemsSeen.Where(r => r.MerchantName == merchant).ToList();

    private static bool HasNoGroups(JsonElement data)
        => !data.TryGetProperty("groups", out var groups) || groups.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task Disabled_FlagKeepsOldBehaviour_AndNeverCallsClassifyItems()
    {
        const string merchant = "Split Disabled Mart";
        FakeAIServiceClient.SetBill(merchant, new BillClassifyResponse("food", "dining", 0.8, "brand"));
        var client = ClientWith(Bill(merchant), splitEnabled: false);
        var token = await RegisterAndLoginAsync(client);

        var data = await ScanAsync(client, token);

        HasNoGroups(data).Should().BeTrue();
        data.GetProperty("amountCents").GetInt64().Should().Be(82_000);
        data.GetProperty("categorySlug").GetString().Should().Be("food");
        FakeAIServiceClient.ClassifyItemsSeen.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemsNotOk_FallsBackToOldPath()
    {
        const string merchant = "Split NotOk Mart";
        FakeAIServiceClient.SetBill(merchant, new BillClassifyResponse("food", "dining", 0.8, "brand"));
        var client = ClientWith(Bill(merchant, itemsOk: false), splitEnabled: true);
        var token = await RegisterAndLoginAsync(client);

        var data = await ScanAsync(client, token);

        HasNoGroups(data).Should().BeTrue();
        data.GetProperty("categorySlug").GetString().Should().Be("food");
        FakeAIServiceClient.ClassifyItemsSeen.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemsOkWithTwoGroups_ReturnsGroupsAndStoresItems()
    {
        const string merchant = "Split Two Groups Mart";
        var client = ClientWith(Bill(merchant), splitEnabled: true);
        var token = await RegisterAndLoginAsync(client);

        var data = await ScanAsync(client, token);

        data.GetProperty("amountCents").GetInt64().Should().Be(82_000);
        data.GetProperty("categorySlug").GetString().Should().Be("shopping");
        var groups = data.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => (g.GetProperty("categorySlug").GetString(), g.GetProperty("amountCents").GetInt64()))
            .Should().Equal(("shopping", 47_000L), ("food", 35_000L));
        groups[1].GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())
            .Should().Equal("Cà phê sữa", "Trân châu");

        var scanId = data.GetProperty("scanId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var items = await db.ReceiptScanItems.AsNoTracking().Where(i => i.ScanId == scanId).OrderBy(i => i.Position).ToListAsync();
        items.Select(i => (i.Position, i.Name, i.AmountCents, i.CategorySlug, i.CategorySource, i.ParentPosition)).Should().Equal(
            (0, "Cà phê sữa", 30_000L, "food", "rule", (int?)null),
            (1, "Trân châu", 5_000L, "food", "rule", 0),
            (2, "Giảm giá", -5_000L, (string?)null, "discount", null),
            (3, "VAT 8%", 2_000L, null, "surcharge", null),
            (4, "Dầu gội", 50_000L, "shopping", "rule", null));
        var scan = await db.ReceiptScans.AsNoTracking().SingleAsync(s => s.Id == scanId);
        scan.CategorySlug.Should().Be("shopping");
        scan.AmountCents.Should().Be(82_000);

        // Chỉ tên món thường, parent đánh lại theo chỉ số món thường.
        SeenFor(merchant).Single().Items.Should().Equal(
            new ClassifyItemInput("Cà phê sữa", null), new ClassifyItemInput("Trân châu", 0), new ClassifyItemInput("Dầu gội", null));
    }

    [Fact]
    public async Task OneKnownCategoryPlusUnknown_ReturnsNoGroupsAndTheOldCategory()
    {
        const string merchant = "Split One Known Mart";
        FakeAIServiceClient.SetBill(merchant, new BillClassifyResponse("food", "dining", 0.8, "brand"));
        FakeAIServiceClient.ClassifyItemsHandler = r => new ClassifyItemsResponse(
            r.Items.Select((item, i) => item.Name == "Dầu gội"
                ? new ClassifiedItem(i, null, "unknown")
                : new ClassifiedItem(i, "food", "rule")).ToList(), false);
        var client = ClientWith(Bill(merchant), splitEnabled: true);
        var token = await RegisterAndLoginAsync(client);

        var data = await ScanAsync(client, token);

        HasNoGroups(data).Should().BeTrue();
        data.GetProperty("amountCents").GetInt64().Should().Be(82_000);
        data.GetProperty("categorySlug").GetString().Should().Be("food");
        SeenFor(merchant).Should().ContainSingle();

        var scanId = data.GetProperty("scanId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.ReceiptScanItems.AsNoTracking().CountAsync(i => i.ScanId == scanId)).Should().Be(0);
        (await db.ReceiptScans.AsNoTracking().SingleAsync(s => s.Id == scanId)).CategorySource.Should().Be("brand");
    }

    [Fact]
    public async Task ConsentIsReadFromTheDatabaseAtEveryScan()
    {
        const string merchant = "Split Consent Mart";
        var client = ClientWith(Bill(merchant), splitEnabled: true);
        var token = await RegisterAndLoginAsync(client);

        await ScanAsync(client, token);           // mặc định chưa đồng ý
        await SetConsentAsync(client, token, true);
        await ScanAsync(client, token);
        await SetConsentAsync(client, token, false);
        await ScanAsync(client, token);

        SeenFor(merchant).Select(r => r.AllowAi).Should().Equal(false, true, false);
    }
}
