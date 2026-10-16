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
/// API quản trị luật của AI: chỉ admin, đi qua nguyên văn tới AI Service (client giả ghi lại lời gọi), mỗi thay đổi THÀNH CÔNG để lại một dòng audit_logs
/// (đọc và thay đổi thất bại thì không), lỗi của AI Service giữ nguyên mã HTTP.
/// </summary>
[Collection("Integration")]
public class AdminAiRulesControllerTests : IClassFixture<AuthApiFactory>
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

    private readonly RecordingClient _rules = new();
    private readonly HttpClient _client;

    public AdminAiRulesControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_rules);
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Rules Test User"));
        return await LoginAsync(email, "Password123!");
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string? token, string? json = null)
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

    private async Task<List<AuditLogData>> RuleAuditLogsAsync(string adminToken)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminAiRuleChanged}&limit=100", adminToken));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnReadsAndWrites()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/ai-rules/brands", null),
                     (HttpMethod.Put, "/api/v1/admin/ai-rules/brands/x", "{\"category_slug\":\"dining\"}"),
                     (HttpMethod.Post, "/api/v1/admin/ai-rules/preview", "{}"),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _rules.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_ForwardsPathAndQuery_ReturnsTheDataAsIs_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await RuleAuditLogsAsync(admin)).Count;
        _rules.Calls.Clear();
        _rules.Respond = _ => Parse("""{"items":[{"brand_key":"zeta mart"}],"total":1,"limit":5,"offset":0}""");

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-rules/brands?q=zeta&limit=5", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _rules.Calls.Single();
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be("brands");
        call.Query.Should().Contain(new KeyValuePair<string, string?>("q", "zeta")).And.Contain(new KeyValuePair<string, string?>("limit", "5"));
        call.ActorId.Should().BeNull();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("items")[0].GetProperty("brand_key").GetString().Should().Be("zeta mart");
        (await RuleAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task Write_ForwardsTheBodyAndTheAdminId_AndWritesOneAuditRow()
    {
        var admin = await AdminTokenAsync();
        var adminId = await AdminIdAsync(admin);
        var before = (await RuleAuditLogsAsync(admin)).Count;
        _rules.Calls.Clear();
        _rules.Respond = _ => Parse("""{"brand_key":"zeta mart","category_slug":"groceries"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/admin/ai-rules/brands/Zeta%20Mart", admin,
            """{"category_slug":"groceries","display_name":"Zeta","reason":"thêm thương hiệu mới"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _rules.Calls.Single();
        call.Method.Should().Be(HttpMethod.Put);
        call.Path.Should().Be("brands/Zeta%20Mart");
        call.ActorId.Should().Be(adminId);
        call.Body!.Value.GetProperty("category_slug").GetString().Should().Be("groceries");
        var logs = await RuleAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        var row = logs.OrderByDescending(l => l.CreatedAt).First();
        row.UserId.Should().Be(adminId);
        row.Metadata.Should().Contain("brands").And.Contain("Zeta Mart").And.Contain("thêm thương hiệu mới");
    }

    [Fact]
    public async Task ARejectedWrite_KeepsTheStatusAndCode_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await RuleAuditLogsAsync(admin)).Count;
        _rules.Respond = _ => throw new AiRuleException(409, "duplicate", "Từ khóa này đã có và đang bật.");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/ai-rules/keywords", admin,
            """{"kind":"product","category_slug":"dining","keyword":"pho"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("duplicate");
        error.GetProperty("message").GetString().Should().Contain("đã có");
        (await RuleAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task WhenTheAiServiceIsDown_TheAnswerIs503()
    {
        var admin = await AdminTokenAsync();
        _rules.Respond = _ => throw new AIServiceUnavailableException("AI Service không phản hồi.");

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-rules/meta", admin))).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Delete_ForwardsTheNameQuery_And204HasNoBody()
    {
        var admin = await AdminTokenAsync();
        _rules.Calls.Clear();
        _rules.Respond = _ => null;

        var response = await _client.SendAsync(Request(HttpMethod.Delete, "/api/v1/admin/ai-rules/item-cache?name=b%E1%BB%89m%20cho%20b%C3%A9", admin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _rules.Calls.Single().Query.Should().Contain(new KeyValuePair<string, string?>("name", "bỉm cho bé"));
    }

    [Fact]
    public async Task Preview_IsAReadEvenThoughItIsAPost()
    {
        var admin = await AdminTokenAsync();
        var before = (await RuleAuditLogsAsync(admin)).Count;
        _rules.Calls.Clear();
        _rules.Respond = _ => Parse("""{"current":{"bill":null,"item":null},"proposed":null,"changed":false}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/ai-rules/preview", admin, """{"merchant_name":"Highlands"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _rules.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("preview");
        call.ActorId.Should().BeNull();
        (await RuleAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Theory]
    [InlineData("POST", "/api/v1/admin/ai-rules/ai-instructions", "{\"instruction_text\":\"x\"}", 201, "ai-instructions")]
    [InlineData("POST", "/api/v1/admin/ai-rules/ai-instructions/3fa85f64-5717-4562-b3fc-2c963f66afa6/activate", null, 200, "ai-instructions")]
    [InlineData("PATCH", "/api/v1/admin/ai-rules/keywords/3fa85f64-5717-4562-b3fc-2c963f66afa6/active", "{\"is_active\":false}", 200, "keywords")]
    [InlineData("PATCH", "/api/v1/admin/ai-rules/brands/zeta/active", "{\"is_active\":false}", 200, "brands")]
    [InlineData("PUT", "/api/v1/admin/ai-rules/item-cache", "{\"name\":\"bim\",\"category_slug\":\"family\"}", 200, "item-cache")]
    [InlineData("POST", "/api/v1/admin/ai-rules/history/3fa85f64-5717-4562-b3fc-2c963f66afa6/revert", "{\"reason\":\"nhầm\"}", 200, "history")]
    public async Task EveryWriteRoute_IsForwardedAndAudited(string method, string url, string? json, int expectedStatus, string resource)
    {
        var admin = await AdminTokenAsync();
        var before = (await RuleAuditLogsAsync(admin)).Count;
        _rules.Calls.Clear();
        _rules.Respond = _ => Parse("""{"ok":true}""");

        var response = await _client.SendAsync(Request(new HttpMethod(method), url, admin, json));

        ((int)response.StatusCode).Should().Be(expectedStatus);
        _rules.Calls.Single().ActorId.Should().NotBeNull();
        var logs = await RuleAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        logs.OrderByDescending(l => l.CreatedAt).First().Metadata.Should().Contain(resource);
    }
}
