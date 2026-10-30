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
/// API quản trị KEY BÊN THỨ BA: chỉ admin, đi qua nguyên văn tới nhóm <c>secrets</c> của AI Service; mỗi lần ĐẶT / XÓA thành công để lại một dòng audit_logs chỉ có tên key, hành động
/// và lý do, KHÔNG BAO GIỜ có giá trị key; đọc / thử / thất bại thì không để lại dòng nào.
/// </summary>
[Collection("Integration")]
public class AdminSecretsControllerTests : IClassFixture<AuthApiFactory>
{
    private const string SecretValue = "AIzaSyD-example-key-0123456789abcdefXYZ9";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class RecordingClient : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];
        public Func<AiRulesRequest, JsonElement?> Respond { get; set; } = _ => Parse("""{"name":"gemini_api_key","source":"db","last4":"XYZ9"}""");

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

    private readonly RecordingClient _ai = new();
    private readonly HttpClient _client;

    public AdminSecretsControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_ai);
        })).CreateClient();
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Secrets Test User"));
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

    private async Task<List<AuditLogData>> SecretAuditLogsAsync(string adminToken)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminSecretChanged}&limit=100", adminToken));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnEveryEndpoint()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/secrets", null),
                     (HttpMethod.Put, "/api/v1/admin/secrets/gemini_api_key", $$"""{"value":"{{SecretValue}}"}"""),
                     (HttpMethod.Delete, "/api/v1/admin/secrets/gemini_api_key", null),
                     (HttpMethod.Post, "/api/v1/admin/secrets/gemini_api_key/test", null),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _ai.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheListAndTheTest_GoToTheSecretsArea_AndLeaveNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await SecretAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/secrets", admin))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/secrets/gemini_api_key/test", admin))).StatusCode.Should().Be(HttpStatusCode.OK);

        _ai.Calls.Select(c => (c.Area, c.Method, c.Path)).Should().Equal(
            (AiRulesRequest.SecretsArea, HttpMethod.Get, ""),
            (AiRulesRequest.SecretsArea, HttpMethod.Post, "gemini_api_key/test"));
        _ai.Calls.Should().OnlyContain(c => c.ActorId == null);
        (await SecretAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task SettingAKey_ForwardsTheValueAndTheAdminId_AndTheAuditRowNeverContainsTheValue()
    {
        var admin = await AdminTokenAsync();
        var before = (await SecretAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/admin/secrets/gemini_api_key", admin, $$"""{"value":"{{SecretValue}}","reason":"doi key quy 4"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(SecretValue);
        var call = _ai.Calls.Single();
        (call.Area, call.Method, call.Path).Should().Be((AiRulesRequest.SecretsArea, HttpMethod.Put, "gemini_api_key"));
        call.ActorId.Should().NotBeNull();
        call.Body!.Value.GetProperty("value").GetString().Should().Be(SecretValue);  // AI Service nhận được key để mã hóa

        var logs = await SecretAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        var metadata = logs.Select(l => l.Metadata).Single(m => m != null && m.Contains("doi key quy 4"));
        metadata.Should().Contain("gemini_api_key").And.Contain("set");
        logs.Select(l => l.Metadata).Should().NotContain(m => m != null && (m.Contains("AIzaSy") || m.Contains("XYZ9") || m.Contains("example-key")));
    }

    [Fact]
    public async Task DeletingAKey_PassesTheReasonAndWritesOneAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await SecretAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(HttpMethod.Delete, "/api/v1/admin/secrets/openrouter_api_key?reason=dung%20lai%20bien%20moi%20truong", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        (call.Area, call.Method, call.Path).Should().Be((AiRulesRequest.SecretsArea, HttpMethod.Delete, "openrouter_api_key"));
        call.Query.Should().Contain(new KeyValuePair<string, string?>("reason", "dung lai bien moi truong"));
        (await SecretAuditLogsAsync(admin)).Count.Should().Be(before + 1);
    }

    [Fact]
    public async Task AFailedSet_KeepsTheAiServiceStatus_AndWritesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await SecretAuditLogsAsync(admin)).Count;
        _ai.Respond = _ => throw new AiRuleException(422, "secrets_unavailable", "Chưa cấu hình khóa mã hóa.");

        var response = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/admin/secrets/gemini_api_key", admin, $$"""{"value":"{{SecretValue}}"}"""));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("secrets_unavailable").And.NotContain(SecretValue);
        (await SecretAuditLogsAsync(admin)).Count.Should().Be(before);
        _ai.Respond = _ => Parse("""{"ok":true}""");
    }
}
