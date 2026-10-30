using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Middleware;
using FinMate.Application.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Cấu hình hệ thống có phiên bản: lưu tạo phiên bản mới (chặn ghi đè bằng <c>expectedVersion</c>), hoàn tác tạo phiên bản MỚI, app đọc qua <c>/app-config</c> công khai,
/// bảo trì chặn người dùng thường nhưng không chặn admin / đăng nhập / app-config, nhật ký chỉ ghi tên các phần đã đổi. Ca nào bật bảo trì đều tắt lại ở cuối.
/// </summary>
[Collection("Integration")]
public class SystemConfigControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Admin = "/api/v1/admin/system-config";

    private readonly HttpClient _client;

    public SystemConfigControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AuditLogData(Guid Id, Guid? UserId, string EventType, string? Metadata, DateTimeOffset CreatedAt);
    private record AuditLogListBody(bool Success, List<AuditLogData> Data);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private Task<string> AdminTokenAsync() => LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

    private async Task<string> UserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Config Test User"));
        return await LoginAsync(email, "Password123!");
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string? token, string? json = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        return root.GetProperty("data");
    }

    private async Task<int> ActiveVersionAsync(string admin)
        => (await DataOf(await _client.SendAsync(Request(HttpMethod.Get, Admin, admin)))).GetProperty("version").GetInt32();

    private Task<HttpResponseMessage> SaveAsync(string admin, int expected, string content, string note = "doi cau hinh")
        => _client.SendAsync(Request(HttpMethod.Put, Admin, admin, $$"""{"expectedVersion":{{expected}},"content":{{content}},"note":"{{note}}"}"""));

    private async Task<List<AuditLogData>> ConfigAuditLogsAsync(string admin)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminSystemConfigChanged}&limit=100", admin));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnTheAdminEndpoints()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, Admin, null),
                     (HttpMethod.Get, $"{Admin}/versions", null),
                     (HttpMethod.Put, Admin, """{"expectedVersion":0,"content":{},"note":"abc"}"""),
                     (HttpMethod.Post, $"{Admin}/rollback/1", """{"expectedVersion":1,"note":"abc"}"""),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task TheAppConfig_IsPublic_HasAnEtag_AndAnswers304ForTheSameVersion()
    {
        var first = await _client.GetAsync("/api/v1/app-config");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.ToString();
        var data = await DataOf(first);
        data.GetProperty("config").GetProperty("maintenance").GetProperty("enabled").GetBoolean().Should().BeFalse();
        first.Headers.CacheControl!.Public.Should().BeTrue();

        var second = new HttpRequestMessage(HttpMethod.Get, "/api/v1/app-config");
        second.Headers.TryAddWithoutValidation("If-None-Match", etag);
        (await _client.SendAsync(second)).StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Saving_CreatesTheNextVersion_AppConfigSeesIt_AndTheAuditRowNamesOnlyTheChangedSections()
    {
        var admin = await AdminTokenAsync();
        var before = await ActiveVersionAsync(admin);
        var logs = (await ConfigAuditLogsAsync(admin)).Count;

        var response = await SaveAsync(admin, before, """{"theme":{"primaryColor":"#2E7D6B","textScale":1.2}}""", "doi mau chu dao");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataOf(response)).GetProperty("version").GetInt32().Should().Be(before + 1);
        var app = await DataOf(await _client.GetAsync("/api/v1/app-config"));
        app.GetProperty("version").GetInt32().Should().Be(before + 1);
        app.GetProperty("config").GetProperty("theme").GetProperty("primaryColor").GetString().Should().Be("#2E7D6B");

        var after = await ConfigAuditLogsAsync(admin);
        after.Count.Should().Be(logs + 1);
        var metadata = after.Select(l => l.Metadata).Single(m => m != null && m.Contains("doi mau chu dao"));
        metadata.Should().Contain("theme").And.NotContain("#2E7D6B");  // chỉ tên phần đã đổi, không chép giá trị
    }

    [Fact]
    public async Task ASaveWithAStaleVersion_Is409_AnInvalidDocumentIs400_AndAMissingNoteIs422()
    {
        var admin = await AdminTokenAsync();
        var current = await ActiveVersionAsync(admin);

        var stale = await SaveAsync(admin, current + 5, """{}""");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("ADMIN_SYSTEM_CONFIG_STALE");

        var invalid = await SaveAsync(admin, current, """{"theme":{"primaryColor":"red"}}""");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noNote = await SaveAsync(admin, current, """{}""", "x");
        noNote.StatusCode.Should().Be((HttpStatusCode)422);

        (await ActiveVersionAsync(admin)).Should().Be(current);  // không lần nào trong ba lần làm đổi cấu hình
    }

    [Fact]
    public async Task ARollback_CreatesANewVersionWithTheOldContent_AndTheHistoryStaysLinear()
    {
        var admin = await AdminTokenAsync();
        var start = await ActiveVersionAsync(admin);
        await SaveAsync(admin, start, """{"theme":{"primaryColor":"#111111"}}""", "ban A");
        await SaveAsync(admin, start + 1, """{"theme":{"primaryColor":"#222222"}}""", "ban B");

        var rollback = await _client.SendAsync(Request(HttpMethod.Post, $"{Admin}/rollback/{start + 1}", admin, $$"""{"expectedVersion":{{start + 2}},"note":"quay ve ban A"}"""));

        rollback.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await DataOf(rollback);
        view.GetProperty("version").GetInt32().Should().Be(start + 3);
        view.GetProperty("content").GetProperty("theme").GetProperty("primaryColor").GetString().Should().Be("#111111");

        var versions = await DataOf(await _client.SendAsync(Request(HttpMethod.Get, $"{Admin}/versions?limit=3", admin)));
        var items = versions.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("version").GetInt32()).Should().Equal(start + 3, start + 2, start + 1);
        items.Select(i => i.GetProperty("status").GetString()).Should().Equal("active", "archived", "archived");  // đúng MỘT bản đang chạy
    }

    [Fact]
    public async Task Maintenance_Blocks_APlainUser_ButNotAnAdmin_Login_OrTheAppConfig()
    {
        var admin = await AdminTokenAsync();
        var user = await UserTokenAsync();
        var current = await ActiveVersionAsync(admin);
        try
        {
            (await SaveAsync(admin, current, """{"maintenance":{"enabled":true,"title":"Bảo trì","message":"Quay lại sau 10 phút"}}""", "bao tri thu")).StatusCode.Should().Be(HttpStatusCode.OK);

            var blocked = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", user));
            blocked.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var error = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
            error.GetProperty("code").GetString().Should().Be(MaintenanceMiddleware.ErrorCode);
            error.GetProperty("message").GetString().Should().Be("Quay lại sau 10 phút");

            (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", admin))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await _client.GetAsync("/api/v1/app-config")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SaveAsync(admin, await ActiveVersionAsync(admin), """{"maintenance":{"enabled":false}}""", "tat bao tri");
        }

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", user))).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
