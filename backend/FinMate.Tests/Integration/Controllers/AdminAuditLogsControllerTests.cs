using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class AdminAuditLogsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AdminAuditLogsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AuditLogData(Guid Id, Guid? UserId, string EventType, string? Metadata, DateTimeOffset CreatedAt);
    private record AuditLogListBody(bool Success, List<AuditLogData> Data, MetaData? Meta);
    private record MetaData(string? Cursor);

    private async Task<string> LoginAsAdminAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK, "AdminUserSeeder phải tạo được tài khoản admin");

        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Audit Test User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
    }

    private HttpRequestMessage Get(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task AnOrdinaryUserCannotReadAuditLogs()
    {
        // Test quan trọng nhất của Phase 8: thiếu [Authorize(Policy = AdminOnly)] thì endpoint
        // rơi về FallbackPolicy và trả 200 cho bất kỳ ai đã đăng nhập — không có gì báo lỗi.
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Get("/api/v1/admin/audit-logs", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousRequestIsRejected()
    {
        var response = await _client.GetAsync("/api/v1/admin/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminSeesLoginEventsAndCanPageThroughThem()
    {
        // Mỗi lần đăng ký + đăng nhập ghi 2 dòng audit, nên chỉ cần vài user là đủ phân trang.
        for (var i = 0; i < 3; i++)
        {
            await RegisterAndLoginAsync();
        }

        var token = await LoginAsAdminAsync();

        var first = await _client.SendAsync(Get("/api/v1/admin/audit-logs?limit=2", token));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPage = await first.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions);
        firstPage!.Data.Should().HaveCount(2);
        firstPage.Meta!.Cursor.Should().NotBeNullOrEmpty("còn dữ liệu thì phải có con trỏ trang sau");

        var second = await _client.SendAsync(
            Get($"/api/v1/admin/audit-logs?limit=2&cursor={Uri.EscapeDataString(firstPage.Meta.Cursor!)}", token));
        var secondPage = await second.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions);

        // Hai trang không được chồng lấn — đây là thứ phân trang keyset phải đảm bảo.
        secondPage!.Data.Select(x => x.Id).Should().NotIntersectWith(firstPage.Data.Select(x => x.Id));
        secondPage.Data.Should().OnlyContain(x => firstPage.Data.All(f => x.CreatedAt <= f.CreatedAt));
    }

    [Fact]
    public async Task FilteringByEventTypeReturnsOnlyThatEvent()
    {
        await RegisterAndLoginAsync();
        var token = await LoginAsAdminAsync();

        var response = await _client.SendAsync(
            Get("/api/v1/admin/audit-logs?eventType=Auth.User.Registered&limit=50", token));
        var body = await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions);

        body!.Data.Should().NotBeEmpty();
        body.Data.Should().OnlyContain(x => x.EventType == "Auth.User.Registered");
    }
}
