using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class MarketDataTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeWeb _web = new();
    private readonly FakeAi _ai = new();

    public MarketDataTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebPageFetcher>();
            services.AddSingleton<IWebPageFetcher>(_web);
            services.RemoveAll<IMarketAiClient>();
            services.AddSingleton<IMarketAiClient>(_ai);
            services.RemoveAll<IMarketAlertTrigger>();
            services.AddSingleton<IMarketAlertTrigger>(new NoAlertTrigger());
        }));
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await InDbAsync(async db =>
        {
            await db.MarketEvents.ExecuteDeleteAsync();
            await db.MarketDocuments.ExecuteDeleteAsync();
            await db.MarketSources.ExecuteDeleteAsync();
            await db.FuelPrices.ExecuteDeleteAsync();
            return 0;
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FakeWeb : IWebPageFetcher
    {
        public Dictionary<string, WebFetchResult> Pages { get; } = new();
        public List<string> Requests { get; } = [];

        public void Html(string url, string body) => Pages[url] = new WebFetchResult(WebFetchOutcome.Ok, body, "text/html", new Uri(url), 200);

        public Task<WebFetchResult> FetchAsync(Uri uri, CancellationToken ct = default)
        {
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(Pages.TryGetValue(uri.AbsoluteUri, out var page) ? page : new WebFetchResult(WebFetchOutcome.NotFound, StatusCode: 404));
        }
    }

    private sealed class NoAlertTrigger : IMarketAlertTrigger
    {
        public void Enqueue()
        {
        }
    }

    private sealed class FakeAi : IMarketAiClient
    {
        public Func<string, string, MarketAiResult> Respond { get; set; } = (_, _) => new MarketAiResult(MarketAiStatus.Ok, []);
        public List<string> Titles { get; } = [];

        public Task<MarketAiResult> ExtractAsync(string title, string text, DateTimeOffset? publishedAt, CancellationToken ct = default)
        {
            Titles.Add(title);
            return Task.FromResult(Respond(title, text));
        }
    }

    private static string Table(long ron95Level3 = 21150) => $"""
        <html><body><table>
        <tr><td>Xăng E10 RON 95 mức 5</td><td>22.650</td><td>23.100</td></tr>
        <tr><td>Xăng E10 RON 95 mức 3</td><td>{ron95Level3:N0}</td><td>{ron95Level3 + 420:N0}</td></tr>
        <tr><td>Xăng E5 RON 92 mức 2</td><td>20.890</td><td>21.300</td></tr>
        </table></body></html>
        """;

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task<string> AdminTokenAsync(UserRole role = UserRole.Admin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now, EmailVerifiedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<(HttpStatusCode Status, JsonElement Root)> PostSourceAsync(string url, string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/market-sources", token, new { url });
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    private async Task<MarketRefreshSummary> RefreshAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMarketDataService>().RefreshAsync();
    }

    private async Task AddSourceAsync(string url, MarketSourceKind kind, bool aiAllowed = true)
    {
        await InDbAsync(async db =>
        {
            db.MarketSources.Add(new MarketSource
            {
                Id = Guid.NewGuid(),
                Name = url,
                Url = url,
                Kind = kind,
                AiAllowed = aiAllowed,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
    }

    private Task<List<FuelPrice>> PricesAsync() => InDbAsync(db => db.FuelPrices.AsNoTracking().ToListAsync());

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.OpsManager)]
    public async Task OnlyAdminsCanUseIt(UserRole role)
    {
        var token = await AdminTokenAsync(role);

        (await SendAsync(HttpMethod.Get, "/api/v1/admin/market-sources", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/v1/admin/market-sources", token, new { url = "https://a.vn/" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/fuel-prices", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _web.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAdminSendsAUrlAndItsKindIsDetected()
    {
        var token = await AdminTokenAsync();
        _web.Html("https://gia.vn/xang", Table());
        _web.Html("https://bao.vn/rss", "<rss><channel><item><title>Giá xăng</title><link>https://bao.vn/a</link></item></channel></rss>");
        _web.Html("https://tin.vn/kinh-te", """<html><body><a href="/gia-xang-tang">Giá xăng tăng</a></body></html>""");

        var (status, root) = await PostSourceAsync("https://gia.vn/xang#bang", token);
        status.Should().Be(HttpStatusCode.Created);
        root.GetProperty("data").GetProperty("source").GetProperty("kind").GetString().Should().Be("price_table");
        root.GetProperty("data").GetProperty("source").GetProperty("url").GetString().Should().Be("https://gia.vn/xang");
        root.GetProperty("data").GetProperty("source").GetProperty("name").GetString().Should().Be("gia.vn");

        (await PostSourceAsync("https://bao.vn/rss", token)).Root.GetProperty("data").GetProperty("source").GetProperty("kind").GetString().Should().Be("feed");
        (await PostSourceAsync("https://tin.vn/kinh-te", token)).Root.GetProperty("data").GetProperty("source").GetProperty("kind").GetString().Should().Be("page");
    }

    [Theory]
    [InlineData("http://gia.vn/", "MARKET_SOURCE_URL_INVALID")]
    [InlineData("không phải link", "MARKET_SOURCE_URL_INVALID")]
    public async Task BadUrlsNeverReachTheNetwork(string url, string code)
    {
        var (status, root) = await PostSourceAsync(url, await AdminTokenAsync());

        status.Should().Be((HttpStatusCode)422);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        _web.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ASiteForbiddenByRobotsIsNotAddedAndNotRead()
    {
        _web.Html("https://www.facebook.com/robots.txt", "User-agent: *\nDisallow: /");

        var (status, root) = await PostSourceAsync("https://www.facebook.com/groups/123", await AdminTokenAsync());

        status.Should().Be((HttpStatusCode)422);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(MarketErrorCodes.RobotsDisallowed);
        _web.Requests.Should().ContainSingle().Which.Should().EndWith("/robots.txt");
    }

    [Fact]
    public async Task AnInternalAddressIsReportedAsSuch()
    {
        _web.Pages["https://nha.vn/robots.txt"] = new WebFetchResult(WebFetchOutcome.AddressNotAllowed);

        var (status, root) = await PostSourceAsync("https://nha.vn/", await AdminTokenAsync());

        status.Should().Be((HttpStatusCode)422);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(MarketErrorCodes.AddressNotAllowed);
    }

    [Fact]
    public async Task ASiteThatRefusesIsReportedAndNotAdded()
    {
        _web.Pages["https://chan.vn/"] = new WebFetchResult(WebFetchOutcome.Refused, StatusCode: 403);

        var (status, root) = await PostSourceAsync("https://chan.vn/", await AdminTokenAsync());

        status.Should().Be((HttpStatusCode)422);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(MarketErrorCodes.Refused);
    }

    [Fact]
    public async Task TheSameUrlTwiceConflicts()
    {
        var token = await AdminTokenAsync();
        _web.Html("https://gia.vn/xang", Table());
        await PostSourceAsync("https://gia.vn/xang", token);

        (await PostSourceAsync("https://gia.vn/xang", token)).Status.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ASiteBlockingAiBotsIsAddedWithAiOff()
    {
        _web.Html("https://bao.vn/robots.txt", "User-agent: GPTBot\nDisallow: /\n\nUser-agent: *\nDisallow:");
        _web.Html("https://bao.vn/rss", "<rss><channel/></rss>");

        var (status, root) = await PostSourceAsync("https://bao.vn/rss", await AdminTokenAsync());

        status.Should().Be(HttpStatusCode.Created);
        root.GetProperty("data").GetProperty("source").GetProperty("aiAllowed").GetBoolean().Should().BeFalse();
        root.GetProperty("data").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain("AI_NOT_ALLOWED");
    }

    [Fact]
    public async Task AnEmptyAppShellWarnsThatThereIsNoContent()
    {
        _web.Html("https://spa.vn/", "<html><body><div id=\"root\"></div></body></html>");

        var (status, root) = await PostSourceAsync("https://spa.vn/", await AdminTokenAsync());

        status.Should().Be(HttpStatusCode.Created);
        root.GetProperty("data").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain(MarketErrorCodes.NoContent);
    }

    [Fact]
    public async Task BulkReportsEachLinkSeparately()
    {
        _web.Html("https://gia.vn/xang", Table());

        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/market-sources/bulk", await AdminTokenAsync(), new { urls = new[] { "https://gia.vn/xang", "http://x.vn", "https://mat.vn/" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        items[0].GetProperty("created").GetBoolean().Should().BeTrue();
        items[1].GetProperty("errorCode").GetString().Should().Be(MarketErrorCodes.InvalidUrl);
        items[2].GetProperty("errorCode").GetString().Should().Be(MarketErrorCodes.NotFound);
    }

    [Fact]
    public async Task BulkOverTheLimitIsRefused()
    {
        var urls = Enumerable.Range(0, MarketDataService.MaxBulkUrls + 1).Select(i => $"https://a{i}.vn/").ToArray();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/market-sources/bulk", await AdminTokenAsync(), new { urls });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        _web.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TestReadsWithoutSaving()
    {
        var token = await AdminTokenAsync();
        _web.Html("https://gia.vn/xang", Table());
        var (_, created) = await PostSourceAsync("https://gia.vn/xang", token);
        var id = created.GetProperty("data").GetProperty("source").GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-sources/{id}/test", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        data.GetProperty("fuelPrices").GetArrayLength().Should().Be(3);
        data.GetProperty("fuelErrorCode").ValueKind.Should().Be(JsonValueKind.Null);
        (await PricesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoSourcesAgreeing_ConfirmOncePerFuel_AndARerunAddsNothing()
    {
        _web.Html("https://a.vn/", Table());
        _web.Html("https://b.vn/", Table());
        await AddSourceAsync("https://a.vn/", MarketSourceKind.PriceTable);
        await AddSourceAsync("https://b.vn/", MarketSourceKind.PriceTable);

        var first = await RefreshAsync();
        var second = await RefreshAsync();

        first.ConfirmedFuels.Should().HaveCount(3);
        second.ConfirmedFuels.Should().BeEmpty();
        var prices = await PricesAsync();
        prices.Should().HaveCount(3);
        prices.Should().OnlyContain(p => p.Origin == FuelPriceOrigin.Sources);
        prices.Single(p => p.FuelKey == FuelKeys.E10Ron95Level3).Region1PriceCents.Should().Be(21150);
    }

    [Fact]
    public async Task APriceChange_WaitsUntilBothSourcesShowIt()
    {
        _web.Html("https://a.vn/", Table());
        _web.Html("https://b.vn/", Table());
        await AddSourceAsync("https://a.vn/", MarketSourceKind.PriceTable);
        await AddSourceAsync("https://b.vn/", MarketSourceKind.PriceTable);
        await RefreshAsync();

        _web.Html("https://a.vn/", Table(21650));
        (await RefreshAsync()).ConfirmedFuels.Should().BeEmpty("nguồn kia chưa cập nhật");

        _web.Html("https://b.vn/", Table(21650));
        (await RefreshAsync()).ConfirmedFuels.Should().ContainSingle().Which.Should().Be(FuelKeys.E10Ron95Level3);

        (await PricesAsync()).Where(p => p.FuelKey == FuelKeys.E10Ron95Level3).Should().HaveCount(2);
    }

    [Fact]
    public async Task ARedesignedPage_RecordsAFailureAndConfirmsNothing()
    {
        _web.Html("https://a.vn/", "<html><body>Giao diện mới</body></html>");
        _web.Html("https://b.vn/", Table());
        await AddSourceAsync("https://a.vn/", MarketSourceKind.PriceTable);
        await AddSourceAsync("https://b.vn/", MarketSourceKind.PriceTable);

        var summary = await RefreshAsync();

        summary.SourcesFailed.Should().Be(1);
        summary.ConfirmedFuels.Should().BeEmpty();
        var source = await InDbAsync(db => db.MarketSources.AsNoTracking().SingleAsync(s => s.Url == "https://a.vn/"));
        source.LastErrorCode.Should().Be(FuelErrorCodes.MissingFuels);
        source.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task OneBrokenSourceDoesNotStopTheOthersFromBeingChecked()
    {
        _web.Pages["https://a.vn/"] = new WebFetchResult(WebFetchOutcome.Refused, StatusCode: 403);
        _web.Html("https://b.vn/", Table());
        await AddSourceAsync("https://a.vn/", MarketSourceKind.PriceTable);
        await AddSourceAsync("https://b.vn/", MarketSourceKind.PriceTable);

        await RefreshAsync();

        var sources = await InDbAsync(db => db.MarketSources.AsNoTracking().ToListAsync());
        sources.Single(s => s.Url == "https://a.vn/").LastErrorCode.Should().Be(MarketErrorCodes.Refused);
        sources.Single(s => s.Url == "https://b.vn/").LastSuccessAt.Should().NotBeNull();
    }

    private static string Article(string body) => $"<html><body><article><p>{string.Join(" ", Enumerable.Repeat(body, 12))}</p></article></body></html>";

    [Fact]
    public async Task AFeed_KeepsOnlyRelevantArticles_AndNeverStoresTheirText()
    {
        _web.Html("https://bao.vn/rss", """
            <rss><channel>
            <item><title>Giá xăng tăng 500 đồng</title><link>https://bao.vn/xang</link><pubDate>Thu, 05 Nov 2026 08:00:00 +0700</pubDate></item>
            <item><title>Đội tuyển bóng đá thắng đậm</title><link>https://bao.vn/bong</link></item>
            </channel></rss>
            """);
        _web.Html("https://bao.vn/xang", Article("Nội dung bí mật về giá xăng tăng từ chiều nay."));
        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);

        var summary = await RefreshAsync();

        summary.NewDocuments.Should().Be(1);
        _web.Requests.Should().NotContain("https://bao.vn/bong");
        var document = (await InDbAsync(db => db.MarketDocuments.AsNoTracking().ToListAsync())).Single();
        document.Url.Should().Be("https://bao.vn/xang");
        document.Title.Should().Be("Giá xăng tăng 500 đồng");
        document.Status.Should().Be(MarketDocumentStatus.Processed);
        document.TextLength.Should().BeGreaterThan(HtmlTextExtractor.MinArticleLength);
        document.ContentHash.Should().NotContain("bí mật");
        JsonSerializer.Serialize(document).Should().NotContain("Nội dung bí mật");
    }

    [Fact]
    public async Task AnArticleIsNotReadTwice()
    {
        const string feedV1 = "<rss><channel><item><title>Giá xăng tăng</title><link>https://bao.vn/xang</link></item></channel></rss>";
        const string feedV2 = "<rss><channel><item><title>Giá xăng tăng</title><link>https://bao.vn/xang</link></item><item><title>Giá điện tăng</title><link>https://bao.vn/dien</link></item></channel></rss>";
        _web.Html("https://bao.vn/rss", feedV1);
        _web.Html("https://bao.vn/xang", Article("Giá xăng tăng từ chiều nay."));
        _web.Html("https://bao.vn/dien", Article("Giá điện tăng từ tháng sau."));
        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);
        await RefreshAsync();

        _web.Requests.Clear();
        await RefreshAsync();
        _web.Requests.Should().NotContain("https://bao.vn/xang", "trang không đổi");

        _web.Html("https://bao.vn/rss", feedV2);
        _web.Requests.Clear();
        (await RefreshAsync()).NewDocuments.Should().Be(1);
        _web.Requests.Should().NotContain("https://bao.vn/xang", "bài đã đọc").And.Contain("https://bao.vn/dien");
    }

    [Fact]
    public async Task APageOnlyFollowsLinksOnItsOwnSite()
    {
        _web.Html("https://tin.vn/kinh-te", """
            <html><body><a href="/gia-xang-tang">Giá xăng tăng</a> <a href="https://www.tin.vn/gia-dien">Giá điện tăng</a>
            <a href="https://la.vn/gia-xang">Giá xăng ở trang khác</a> <a href="/bong-da">Bóng đá</a></body></html>
            """);
        _web.Html("https://tin.vn/gia-xang-tang", Article("Giá xăng tăng từ chiều nay."));
        _web.Html("https://www.tin.vn/gia-dien", Article("Giá điện tăng từ tháng sau."));
        await AddSourceAsync("https://tin.vn/kinh-te", MarketSourceKind.Page);

        (await RefreshAsync()).NewDocuments.Should().Be(2);

        _web.Requests.Should().NotContain(r => r.Contains("la.vn")).And.NotContain("https://tin.vn/bong-da");
    }

    [Fact]
    public async Task ArticlesThatAreEmptyOrForbiddenAreRecordedAsSuch()
    {
        _web.Html("https://bao.vn/rss", """
            <rss><channel>
            <item><title>Giá xăng rỗng</title><link>https://bao.vn/rong</link></item>
            <item><title>Giá điện bị cấm</title><link>https://bao.vn/cam/dien</link></item>
            <item><title>Giá nước từ chối</title><link>https://bao.vn/403</link></item>
            </channel></rss>
            """);
        _web.Html("https://bao.vn/robots.txt", "User-agent: *\nDisallow: /cam/");
        _web.Html("https://bao.vn/rong", "<html><body><div id=\"root\"></div></body></html>");
        _web.Pages["https://bao.vn/403"] = new WebFetchResult(WebFetchOutcome.Refused, StatusCode: 403);
        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);

        await RefreshAsync();

        var documents = await InDbAsync(db => db.MarketDocuments.AsNoTracking().ToListAsync());
        documents.Single(d => d.Url.EndsWith("rong")).Status.Should().Be(MarketDocumentStatus.NoContent);
        documents.Single(d => d.Url.EndsWith("dien")).Status.Should().Be(MarketDocumentStatus.Blocked);
        documents.Single(d => d.Url.EndsWith("403")).Status.Should().Be(MarketDocumentStatus.Blocked);
        _web.Requests.Should().NotContain("https://bao.vn/cam/dien");
    }

    [Fact]
    public async Task ATemporaryArticleFailureIsRetriedNextRun()
    {
        _web.Html("https://bao.vn/rss", "<rss><channel><item><title>Giá xăng tăng</title><link>https://bao.vn/xang</link></item></channel></rss>");
        _web.Pages["https://bao.vn/xang"] = new WebFetchResult(WebFetchOutcome.Failed);
        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);

        (await RefreshAsync()).NewDocuments.Should().Be(0);

        _web.Html("https://bao.vn/xang", Article("Giá xăng tăng từ chiều nay."));
        (await RefreshAsync()).NewDocuments.Should().Be(1);
    }

    [Fact]
    public async Task ArticlesOverThePerRunLimitAreReadNextRunEvenIfTheFeedIsUnchanged()
    {
        var count = new MarketDataSettings().MaxDocumentsPerRun + 3;
        var items = string.Concat(Enumerable.Range(0, count).Select(i => $"<item><title>Giá xăng {i}</title><link>https://bao.vn/{i}</link></item>"));
        _web.Html("https://bao.vn/rss", $"<rss><channel>{items}</channel></rss>");
        foreach (var i in Enumerable.Range(0, count))
        {
            _web.Html($"https://bao.vn/{i}", Article("Giá xăng tăng từ chiều nay."));
        }

        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);

        (await RefreshAsync()).NewDocuments.Should().Be(new MarketDataSettings().MaxDocumentsPerRun);
        (await RefreshAsync()).NewDocuments.Should().Be(3);
        (await RefreshAsync()).NewDocuments.Should().Be(0);
    }

    [Fact]
    public async Task AnInactiveSourceIsNotRead()
    {
        var token = await AdminTokenAsync();
        _web.Html("https://gia.vn/xang", Table());
        var (_, created) = await PostSourceAsync("https://gia.vn/xang", token);
        var id = created.GetProperty("data").GetProperty("source").GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Patch, $"/api/v1/admin/market-sources/{id}", token, new { isActive = false, kind = "page", name = "Trang giá" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _web.Requests.Clear();
        (await RefreshAsync()).SourcesChecked.Should().Be(0);
        _web.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AManualCorrection_NeedsANote_AndIsAudited()
    {
        var token = await AdminTokenAsync();
        const string url = "/api/v1/admin/fuel-prices";

        (await SendAsync(HttpMethod.Post, url, token, new { fuelKey = FuelKeys.E10Ron95Level3, region1PriceCents = 21150, region2PriceCents = 21570 })).StatusCode.Should().Be((HttpStatusCode)422);
        (await SendAsync(HttpMethod.Post, url, token, new { fuelKey = "bia", region1PriceCents = 21150, region2PriceCents = 21570, note = "sửa" })).StatusCode.Should().Be((HttpStatusCode)422);
        (await SendAsync(HttpMethod.Post, url, token, new { fuelKey = FuelKeys.E10Ron95Level3, region1PriceCents = 500, region2PriceCents = 21570, note = "sửa" })).StatusCode.Should().Be((HttpStatusCode)422);

        var ok = await SendAsync(HttpMethod.Post, url, token, new { fuelKey = FuelKeys.E10Ron95Level3, region1PriceCents = 21150, region2PriceCents = 21570, note = "Nguồn hỏng" });
        ok.StatusCode.Should().Be(HttpStatusCode.Created);

        (await SendAsync(HttpMethod.Post, url, token, new { fuelKey = FuelKeys.E10Ron95Level3, region1PriceCents = 21150, region2PriceCents = 21570, note = "lặp" })).StatusCode.Should().Be((HttpStatusCode)422);
        (await SendAsync(HttpMethod.Post, url, token, new { fuelKey = FuelKeys.E10Ron95Level3, region1PriceCents = 40000, region2PriceCents = 41000, note = "nhảy" })).StatusCode.Should().Be((HttpStatusCode)422);

        var price = (await PricesAsync()).Single();
        price.Origin.Should().Be(FuelPriceOrigin.Manual);
        (await InDbAsync(db => db.AuditLogs.AsNoTracking().CountAsync(a => a.EventType == "Admin.FuelPrice.Corrected"))).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ThePublicConfigDoesNotRevealOperatingSettings()
    {
        var adminId = await InDbAsync(db => db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).FirstAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
            var current = await config.GetActiveAsync();
            await config.SaveAsync(adminId, null, current.Version, current.Content with { MarketData = new MarketDataSettings { MaxChangePercent = 33 } }, "thử cấu hình");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ISystemConfigProvider>().Invalidate();
        }

        var body = await (await _client.GetAsync("/api/v1/app-config")).Content.ReadAsStringAsync();

        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("config").GetProperty("marketData").GetProperty("maxChangePercent").GetInt32()
            .Should().Be(new MarketDataSettings().MaxChangePercent, "app chỉ thấy giá trị mặc định");
    }

    [Fact]
    public async Task OldDocumentsArePurged()
    {
        await AddSourceAsync("https://bao.vn/rss", MarketSourceKind.Feed);
        var sourceId = await InDbAsync(db => db.MarketSources.Select(s => s.Id).SingleAsync());
        await InDbAsync(async db =>
        {
            db.MarketDocuments.Add(new MarketDocument { Id = Guid.NewGuid(), SourceId = sourceId, Url = "https://bao.vn/cu", Title = "Cũ", Status = MarketDocumentStatus.PendingAi, FetchedAt = DateTimeOffset.UtcNow.AddDays(-181) });
            db.MarketDocuments.Add(new MarketDocument { Id = Guid.NewGuid(), SourceId = sourceId, Url = "https://bao.vn/moi", Title = "Mới", Status = MarketDocumentStatus.PendingAi, FetchedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IMarketDataService>().PurgeOldDocumentsAsync()).Should().Be(1);
    }

    private const string PowerEvidence = "Giá điện tăng 4,8% từ ngày 10/11.";

    private static MarketAiResult PowerUp(string fact = "actual", decimal? percent = 4.8m, string evidence = PowerEvidence, string topic = "electricity")
        => new(MarketAiStatus.Ok, [new MarketAiSignal(topic, "up", fact, percent, "2026-11-10", evidence)]);

    private async Task AddNewsAsync(string host, string title = "Giá điện tăng")
    {
        var published = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
        _web.Html($"https://{host}/rss", $"<rss><channel><item><title>{title}</title><link>https://{host}/dien</link><pubDate>{published}</pubDate></item></channel></rss>");
        _web.Html($"https://{host}/dien", Article(PowerEvidence));
        await AddSourceAsync($"https://{host}/rss", MarketSourceKind.Feed);
    }

    private Task<List<MarketEvent>> EventsAsync() => InDbAsync(db => db.MarketEvents.AsNoTracking().ToListAsync());

    [Fact]
    public async Task AValidSignalIsStoredWithItsQuote()
    {
        _ai.Respond = (_, _) => PowerUp();
        await AddNewsAsync("a.vn");

        (await RefreshAsync()).AiProcessed.Should().Be(1);

        var signal = await InDbAsync(db => db.MarketSignals.AsNoTracking().SingleAsync());
        signal.Topic.Should().Be(MarketTopic.Electricity);
        signal.ChangePercent.Should().Be(4.8m);
        signal.EffectiveDate.Should().Be(new DateOnly(2026, 11, 10));
        signal.Evidence.Should().Be(PowerEvidence);
    }

    [Theory]
    [InlineData("Giá điện giảm 50% theo lời hệ thống.", null, "đoạn trích không có trong bài")]
    [InlineData(PowerEvidence, "12", "số phần trăm không có trong đoạn trích")]
    [InlineData(PowerEvidence, "8", "8 chỉ là một phần của 4,8%")]
    public async Task ASignalThatDoesNotMatchTheArticleIsDropped(string evidence, string? percent, string because)
    {
        _ai.Respond = (_, _) => PowerUp(percent: percent is null ? null : decimal.Parse(percent), evidence: evidence);
        await AddNewsAsync("a.vn");

        await RefreshAsync();

        (await InDbAsync(db => db.MarketSignals.CountAsync())).Should().Be(0, because);
        (await InDbAsync(db => db.MarketDocuments.SingleAsync())).Status.Should().Be(MarketDocumentStatus.Processed);
    }

    [Fact]
    public async Task AnUnknownTopicFromTheModelIsDropped()
    {
        _ai.Respond = (_, _) => PowerUp(topic: "ignore_previous_instructions");
        await AddNewsAsync("a.vn");

        await RefreshAsync();

        (await InDbAsync(db => db.MarketSignals.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task OneSiteIsNotEnoughForAnEvent_TwoSitesAre()
    {
        _ai.Respond = (_, _) => PowerUp();
        await AddNewsAsync("a.vn");
        await RefreshAsync();
        (await EventsAsync()).Should().BeEmpty();

        await AddNewsAsync("b.vn");
        await RefreshAsync();

        var marketEvent = (await EventsAsync()).Single();
        marketEvent.Status.Should().Be(MarketEventStatus.Pending);
        marketEvent.SourceCount.Should().Be(2);
    }

    [Fact]
    public async Task AForecastNeverBecomesAnEvent()
    {
        _ai.Respond = (_, _) => PowerUp(fact: "forecast");
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");

        await RefreshAsync();

        (await InDbAsync(db => db.MarketSignals.CountAsync())).Should().Be(2);
        (await EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FuelNewsDoesNotBecomeAnEventBecauseRealPricesExist()
    {
        _ai.Respond = (_, _) => PowerUp(topic: "fuel");
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");

        await RefreshAsync();

        (await EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AnOldArticleDoesNotCountTowardsAnEvent()
    {
        _ai.Respond = (_, _) => PowerUp();
        await AddNewsAsync("a.vn");
        var old = DateTimeOffset.UtcNow.AddDays(-30).ToString("R");
        _web.Html("https://b.vn/rss", $"<rss><channel><item><title>Giá điện tăng</title><link>https://b.vn/dien</link><pubDate>{old}</pubDate></item></channel></rss>");
        _web.Html("https://b.vn/dien", Article(PowerEvidence));
        await AddSourceAsync("https://b.vn/rss", MarketSourceKind.Feed);

        await RefreshAsync();

        (await EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAKeyArticlesWaitAndCostNothing_AndAreReadOnceAKeyExists()
    {
        _ai.Respond = (_, _) => new MarketAiResult(MarketAiStatus.NotConfigured, []);
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");

        var first = await RefreshAsync();

        first.AiStatus.Should().Be("not_configured");
        var documents = await InDbAsync(db => db.MarketDocuments.AsNoTracking().ToListAsync());
        documents.Should().OnlyContain(d => d.Status == MarketDocumentStatus.PendingAi && d.AiAttemptedAt == null);

        _ai.Respond = (_, _) => PowerUp();
        (await RefreshAsync()).AiProcessed.Should().Be(2);
        (await EventsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AnAiFailureLeavesTheArticleWaitingAndStopsTheRun()
    {
        _ai.Respond = (_, _) => new MarketAiResult(MarketAiStatus.Failed, []);
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");

        (await RefreshAsync()).AiStatus.Should().Be("failed");

        _ai.Titles.Should().ContainSingle();
        (await InDbAsync(db => db.MarketDocuments.CountAsync(d => d.Status == MarketDocumentStatus.PendingAi))).Should().Be(2);
    }

    [Fact]
    public async Task ASiteThatForbidsAiIsNeverSentToAi()
    {
        _web.Html("https://bao.vn/robots.txt", "User-agent: GPTBot\nDisallow: /\n\nUser-agent: *\nDisallow:");
        await AddNewsAsync("bao.vn");

        await RefreshAsync();

        _ai.Titles.Should().BeEmpty();
        (await InDbAsync(db => db.MarketDocuments.SingleAsync())).Status.Should().Be(MarketDocumentStatus.AiNotAllowed);
    }

    [Fact]
    public async Task TheDailyLimitStopsAiCalls()
    {
        await AddNewsAsync("a.vn");
        await InDbAsync(async db =>
        {
            var source = await db.MarketSources.FirstAsync();
            for (var i = 0; i < new MarketDataSettings().MaxAiDocumentsPerDay; i++)
            {
                db.MarketDocuments.Add(new MarketDocument { Id = Guid.NewGuid(), SourceId = source.Id, Url = $"https://cu.vn/{i}", Title = "cũ", Status = MarketDocumentStatus.Processed, FetchedAt = DateTimeOffset.UtcNow, AiAttemptedAt = DateTimeOffset.UtcNow });
            }

            return await db.SaveChangesAsync();
        });

        (await RefreshAsync()).AiStatus.Should().Be("daily_limit");

        _ai.Titles.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAdminApprovesOnce_ASecondReviewConflicts()
    {
        _ai.Respond = (_, _) => PowerUp();
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");
        await RefreshAsync();
        var token = await AdminTokenAsync();
        var id = (await EventsAsync()).Single().Id;

        var list = await SendAsync(HttpMethod.Get, "/api/v1/admin/market-events?status=pending", token);
        var item = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("items")[0];
        item.GetProperty("topic").GetString().Should().Be("electricity");
        item.GetProperty("signals").GetArrayLength().Should().Be(2);
        item.GetProperty("signals")[0].GetProperty("evidence").GetString().Should().Be(PowerEvidence);

        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-events/{id}/approve", token, new { note = "Đã kiểm tra EVN" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-events/{id}/reject", token, new { })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-events/{Guid.NewGuid()}/approve", token, new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reviewed = (await EventsAsync()).Single();
        reviewed.Status.Should().Be(MarketEventStatus.Approved);
        reviewed.ReviewNote.Should().Be("Đã kiểm tra EVN");
    }

    [Fact]
    public async Task ARejectedEventIsNotRaisedAgainByRepublishedNews()
    {
        _ai.Respond = (_, _) => PowerUp();
        await AddNewsAsync("a.vn");
        await AddNewsAsync("b.vn");
        await RefreshAsync();
        var id = (await EventsAsync()).Single().Id;
        await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-events/{id}/reject", await AdminTokenAsync(), new { });

        await AddNewsAsync("c.vn");
        await RefreshAsync();

        var marketEvent = (await EventsAsync()).Single();
        marketEvent.Status.Should().Be(MarketEventStatus.Rejected);
        marketEvent.SourceCount.Should().Be(3);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.OpsManager)]
    public async Task OnlyAdminsReviewEvents(UserRole role)
    {
        var token = await AdminTokenAsync(role);

        (await SendAsync(HttpMethod.Get, "/api/v1/admin/market-events", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/market-events/{Guid.NewGuid()}/approve", token, new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
