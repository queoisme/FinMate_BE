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
/// API quản trị CHỈ DẪN AI cho gợi ý phương thức: chỉ admin, đi qua nguyên văn tới AI Service (client giả ghi lại lời gọi), mỗi lần GHI thành công để lại đúng một dòng audit_logs
/// KHÔNG kèm nội dung chỉ dẫn; đọc hoặc bị AI Service từ chối thì không để lại dòng nào.
/// </summary>
[Collection("Integration")]
public class AdminFinanceMethodPromptsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Base = "/api/v1/admin/finance-method-prompts";

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

    private readonly RecordingClient _ai = new();
    private readonly HttpClient _client;

    public AdminFinanceMethodPromptsControllerTests(AuthApiFactory factory)
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Prompts Test User"));
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

    private async Task<List<AuditLogData>> AuditAsync(string adminToken, string eventType)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={eventType}&limit=100", adminToken));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    private async Task<int> PromptAuditCountAsync(string adminToken)
        => (await AuditAsync(adminToken, AuditEvents.AdminMethodPromptCreated)).Count + (await AuditAsync(adminToken, AuditEvents.AdminMethodPromptActivated)).Count;

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnEveryRoute()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, Base, null),
                     (HttpMethod.Post, Base, """{"prompt_version":"v2","instruction_text":"x","activate":false}"""),
                     (HttpMethod.Post, $"{Base}/method-select-v1/activate", "{}"),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _ai.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_GoesToThePromptsArea_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = await PromptAuditCountAsync(admin);
        _ai.Calls.Clear();

        (await _client.SendAsync(Request(HttpMethod.Get, Base, admin))).StatusCode.Should().Be(HttpStatusCode.OK);

        _ai.Calls.Single().Should().Match<AiRulesRequest>(c => c.Area == AiRulesRequest.MethodPromptsArea && c.Method == HttpMethod.Get && c.Path == "" && c.ActorId == null);
        (await PromptAuditCountAsync(admin)).Should().Be(before);
    }

    [Fact]
    public async Task Creating_IsForwardedWithTheAdminAsActor_AndWritesOneAuditRowWithoutTheInstructionText()
    {
        var admin = await AdminTokenAsync();
        var before = (await AuditAsync(admin, AuditEvents.AdminMethodPromptCreated)).Count;
        _ai.Calls.Clear();
        _ai.Respond = _ => Parse("""{"prompt_version":"method-select-v2","instruction_text":"CHỈ DẪN BÍ MẬT","is_active":true}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, Base, admin,
            """{"prompt_version":"method-select-v2","instruction_text":"CHỈ DẪN BÍ MẬT","activate":true}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var call = _ai.Calls.Single();
        (call.Area, call.Method, call.Path).Should().Be((AiRulesRequest.MethodPromptsArea, HttpMethod.Post, ""));
        call.ActorId.Should().NotBeNull();
        var logs = await AuditAsync(admin, AuditEvents.AdminMethodPromptCreated);
        logs.Count.Should().Be(before + 1);
        var metadata = logs.Select(l => l.Metadata).Should().Contain(m => m != null && m.Contains("method-select-v2")).Subject!;
        metadata.Should().NotContain("CHỈ DẪN BÍ MẬT");
        _ai.Respond = _ => Parse("""{"ok":true}""");
    }

    [Fact]
    public async Task Activating_EscapesTheVersion_RecordsPreviousVersion_AndWritesOneAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await AuditAsync(admin, AuditEvents.AdminMethodPromptActivated)).Count;
        _ai.Calls.Clear();
        _ai.Respond = _ => Parse("""{"active_version":"method-select-v1","previous_version":"method-select-v2"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/method-select-v1/activate", admin, """{"expected_active":"method-select-v2"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        (call.Area, call.Method, call.Path).Should().Be((AiRulesRequest.MethodPromptsArea, HttpMethod.Post, "method-select-v1/activate"));
        call.ActorId.Should().NotBeNull();
        var logs = await AuditAsync(admin, AuditEvents.AdminMethodPromptActivated);
        logs.Count.Should().Be(before + 1);
        logs.Select(l => l.Metadata).Should().Contain(m => m != null && m.Contains("method-select-v1") && m.Contains("method-select-v2"));
        _ai.Respond = _ => Parse("""{"ok":true}""");
    }

    [Theory]
    [InlineData("", 409, "prompt_version_exists")]
    [InlineData("/khong-co/activate", 404, "prompt_not_found")]
    public async Task ARefusedWrite_KeepsTheAiServiceStatus_AndWritesNoAuditRow(string suffix, int status, string code)
    {
        var admin = await AdminTokenAsync();
        var before = await PromptAuditCountAsync(admin);
        _ai.Respond = _ => throw new AiRuleException(status, code, "bị từ chối");

        var response = await _client.SendAsync(Request(HttpMethod.Post, Base + suffix, admin,
            """{"prompt_version":"method-select-v1","instruction_text":"x","activate":false}"""));

        ((int)response.StatusCode).Should().Be(status);
        (await PromptAuditCountAsync(admin)).Should().Be(before);
        _ai.Respond = _ => Parse("""{"ok":true}""");
    }
}
