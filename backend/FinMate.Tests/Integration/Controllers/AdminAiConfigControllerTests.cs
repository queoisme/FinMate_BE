using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// API quản trị TÀI LIỆU CẤU HÌNH của AI/OCR: chỉ admin, đi qua nguyên văn tới nhóm <c>config</c> của AI Service (client giả ghi lại lời gọi), mỗi lần lưu nháp / kích hoạt
/// THÀNH CÔNG để lại một dòng audit_logs (đọc, xem trước và thất bại thì không), lỗi của AI Service giữ nguyên mã HTTP.
/// </summary>
[Collection("Integration")]
public class AdminAiConfigControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class RecordingClient : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];
        public Func<AiRulesRequest, JsonElement?> Respond { get; set; } = _ => Parse("""{"ok":true}""");

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private readonly RecordingClient _config = new();
    private readonly HttpClient _client;

    public AdminAiConfigControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_config);
        })).CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AuditLogData(Guid Id, Guid? UserId, string EventType, string? Metadata, DateTimeOffset CreatedAt);
    private record AuditLogListBody(bool Success, List<AuditLogData> Data);
    private record IdBody(bool Success, IdData Data);
    private record IdData(Guid Id);

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

    private async Task<Guid> AdminIdAsync(string token)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", token));
        return (await response.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
    }

    private async Task<List<AuditLogData>> ConfigAuditLogsAsync(string adminToken)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminAiConfigChanged}&limit=100", adminToken));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnReadsAndWrites()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/ai-config/documents", null),
                     (HttpMethod.Get, "/api/v1/admin/ai-config/documents/chat_dictionary", null),
                     (HttpMethod.Post, "/api/v1/admin/ai-config/documents/chat_dictionary/drafts", "{\"content\":{},\"note\":\"abc\"}"),
                     (HttpMethod.Post, "/api/v1/admin/ai-config/documents/chat_dictionary/versions/1/activate", "{\"expected_active_version\":0}"),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _config.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/v1/admin/ai-config/documents", "documents")]
    [InlineData("/api/v1/admin/ai-config/documents/chat_dictionary", "documents/chat_dictionary")]
    [InlineData("/api/v1/admin/ai-config/documents/chat_dictionary/default", "documents/chat_dictionary/default")]
    [InlineData("/api/v1/admin/ai-config/documents/thresholds/versions?limit=5&offset=10", "documents/thresholds/versions")]
    [InlineData("/api/v1/admin/ai-config/documents/app_screens/versions/3", "documents/app_screens/versions/3")]
    public async Task EveryRead_GoesToTheConfigArea_ReturnsTheDataAsIs_AndLeavesNoAuditRow(string url, string expectedPath)
    {
        var admin = await AdminTokenAsync();
        var before = (await ConfigAuditLogsAsync(admin)).Count;
        _config.Calls.Clear();
        _config.Respond = _ => Parse("""{"name":"chat_dictionary","version":0,"source":"file"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Get, url, admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _config.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.ConfigArea);
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be(expectedPath);
        call.ActorId.Should().BeNull();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("source").GetString().Should().Be("file");
        (await ConfigAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task TheVersionListForwardsPagingToTheAiService()
    {
        var admin = await AdminTokenAsync();
        _config.Calls.Clear();

        await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-config/documents/thresholds/versions?limit=5&offset=10", admin));

        _config.Calls.Single().Query.Should().Contain(new KeyValuePair<string, string?>("limit", "5")).And.Contain(new KeyValuePair<string, string?>("offset", "10"));
    }

    [Fact]
    public async Task APathSegmentIsEscapedBeforeItIsForwarded()
    {
        var admin = await AdminTokenAsync();
        _config.Calls.Clear();

        await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-config/documents/ten%20la", admin));
        await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-config/documents/a%2F..%2Fb", admin));

        _config.Calls[0].Path.Should().Be("documents/ten%20la");
        // tên chứa dấu gạch chéo được mã hóa tiếp: vẫn là MỘT đoạn đường dẫn, không thoát ra ngoài /admin/config/documents
        _config.Calls[1].Path.Split('/').Should().HaveCount(2).And.NotContain("..");
    }

    [Fact]
    public async Task SavingADraft_ForwardsTheBodyAndTheAdminId_Returns201_AndWritesOneAuditRowWithTheNewVersion()
    {
        var admin = await AdminTokenAsync();
        var adminId = await AdminIdAsync(admin);
        var before = (await ConfigAuditLogsAsync(admin)).Count;
        _config.Calls.Clear();
        _config.Respond = _ => Parse("""{"name":"chat_dictionary","version":4,"status":"draft","validation":"ok"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/ai-config/documents/chat_dictionary/drafts", admin,
            """{"content":{"dining":{"kind":"expense","label":"Ăn uống","keywords":["pho"]}},"note":"thêm từ phở"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var call = _config.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.ConfigArea);
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("documents/chat_dictionary/drafts");
        call.ActorId.Should().Be(adminId);
        call.Body!.Value.GetProperty("note").GetString().Should().Be("thêm từ phở");
        var logs = await ConfigAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        var row = logs.OrderByDescending(l => l.CreatedAt).First();
        row.UserId.Should().Be(adminId);
        row.Metadata.Should().Contain("chat_dictionary").And.Contain("draft").And.Contain("4").And.Contain("thêm từ phở");
    }

    [Fact]
    public async Task Activating_ForwardsTheOptimisticLock_AndAuditsTheVersionAndTheForceFlag()
    {
        var admin = await AdminTokenAsync();
        var adminId = await AdminIdAsync(admin);
        var before = (await ConfigAuditLogsAsync(admin)).Count;
        _config.Calls.Clear();
        _config.Respond = _ => Parse("""{"name":"merchant_lexicon","version":7,"status":"active","previous_active":6}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/ai-config/documents/merchant_lexicon/versions/7/activate", admin,
            """{"expected_active_version":6,"force":true}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _config.Calls.Single();
        call.Path.Should().Be("documents/merchant_lexicon/versions/7/activate");
        call.ActorId.Should().Be(adminId);
        call.Body!.Value.GetProperty("expected_active_version").GetInt32().Should().Be(6);
        var logs = await ConfigAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        logs.OrderByDescending(l => l.CreatedAt).First().Metadata.Should().Contain("merchant_lexicon").And.Contain("activate").And.Contain("7").And.Contain("true");
    }

    [Fact]
    public async Task Preview_IsAReadEvenThoughItIsAPost_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await ConfigAuditLogsAsync(admin)).Count;
        _config.Calls.Clear();
        _config.Respond = _ => Parse("""{"name":"chat_dictionary","version":2,"preview":{"available":true,"delta":0}}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/ai-config/documents/chat_dictionary/versions/2/preview", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _config.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("documents/chat_dictionary/versions/2/preview");
        call.ActorId.Should().BeNull();
        (await ConfigAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Theory]
    [InlineData(409, "stale_active", "/api/v1/admin/ai-config/documents/chat_dictionary/versions/2/activate", "{\"expected_active_version\":9}", HttpStatusCode.Conflict)]
    [InlineData(422, "preview_regression", "/api/v1/admin/ai-config/documents/chat_dictionary/versions/2/activate", "{\"expected_active_version\":0}", HttpStatusCode.UnprocessableEntity)]
    [InlineData(422, "missing_keys", "/api/v1/admin/ai-config/documents/thresholds/drafts", "{\"content\":{},\"note\":\"abc\"}", HttpStatusCode.UnprocessableEntity)]
    [InlineData(404, "not_found", "/api/v1/admin/ai-config/documents/khong_co/drafts", "{\"content\":{\"a\":1},\"note\":\"abc\"}", HttpStatusCode.NotFound)]
    public async Task ARejectedWrite_KeepsTheStatusAndCode_AndLeavesNoAuditRow(int status, string code, string url, string json, HttpStatusCode expected)
    {
        var admin = await AdminTokenAsync();
        var before = (await ConfigAuditLogsAsync(admin)).Count;
        _config.Respond = _ => throw new AiRuleException(status, code, "Lý do từ AI Service bằng tiếng Việt.");

        var response = await _client.SendAsync(Request(HttpMethod.Post, url, admin, json));

        response.StatusCode.Should().Be(expected);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be(code);
        error.GetProperty("message").GetString().Should().Contain("AI Service");
        (await ConfigAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task WhenTheAiServiceIsDown_TheAnswerIs503()
    {
        var admin = await AdminTokenAsync();
        _config.Respond = _ => throw new AIServiceUnavailableException("AI Service không phản hồi.");

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-config/documents", admin))).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
