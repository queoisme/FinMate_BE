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
/// API quản trị ĐỊNH DẠNG THÔNG BÁO (kèm trạng thái Rasa): chỉ admin, đi qua nguyên văn tới AI Service (client giả ghi lại lời gọi), mỗi lần GHI thành công để lại một dòng audit_logs
/// KHÔNG kèm thân yêu cầu (mẫu có thể chứa chữ thông báo), đọc / thử / thất bại thì không để lại dòng nào.
/// </summary>
[Collection("Integration")]
public class AdminProviderFormatsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid FormatId = Guid.Parse("11111111-2222-3333-4444-555555555555");

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

    public AdminProviderFormatsControllerTests(AuthApiFactory factory)
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Formats Test User"));
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

    private async Task<List<AuditLogData>> FormatAuditLogsAsync(string adminToken)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.AdminProviderFormatChanged}&limit=100", adminToken));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnReadsWritesAndRasa()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/provider-formats", null),
                     (HttpMethod.Post, $"/api/v1/admin/provider-formats/{FormatId}/try", "{\"text\":\"abc\"}"),
                     (HttpMethod.Post, "/api/v1/admin/provider-formats/clone", "{\"package_name\":\"com.x\"}"),
                     (HttpMethod.Get, "/api/v1/admin/rasa/status", null),
                     (HttpMethod.Post, "/api/v1/admin/rasa/parse", "{\"text\":\"abc\"}"),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _ai.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/v1/admin/provider-formats?package_name=com.VCB&status=active&limit=5", "", "package_name")]
    [InlineData("/api/v1/admin/provider-formats/11111111-2222-3333-4444-555555555555", "11111111-2222-3333-4444-555555555555", null)]
    [InlineData("/api/v1/admin/provider-formats/11111111-2222-3333-4444-555555555555/check", "11111111-2222-3333-4444-555555555555/check", null)]
    public async Task EveryRead_GoesToTheFormatsArea_AsIs_AndLeavesNoAuditRow(string url, string expectedPath, string? expectedQueryKey)
    {
        var admin = await AdminTokenAsync();
        var before = (await FormatAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(HttpMethod.Get, url, admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.FormatsArea);
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be(expectedPath);
        call.ActorId.Should().BeNull();
        if (expectedQueryKey is not null)
        {
            call.Query.Should().ContainKey(expectedQueryKey);
        }

        (await FormatAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Fact]
    public async Task TryingANotification_IsForwardedAsAPost_ButWritesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await FormatAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/v1/admin/provider-formats/{FormatId}/try", admin, """{"text":"TK 1|GD: -5,000VND"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        (call.Area, call.Method, call.Path).Should().Be((AiRulesRequest.FormatsArea, HttpMethod.Post, $"{FormatId}/try"));
        call.ActorId.Should().BeNull();
        (await FormatAuditLogsAsync(admin)).Count.Should().Be(before);
    }

    [Theory]
    [InlineData("POST", "clone", "clone", "{\"package_name\":\"com.x\",\"reason\":\"them ban moi\"}", 201)]
    [InlineData("POST", "11111111-2222-3333-4444-555555555555/activate", "activate", "{\"reason\":\"da kiem tra\"}", 200)]
    [InlineData("POST", "11111111-2222-3333-4444-555555555555/patterns", "pattern.add", "{\"pattern_name\":\"p\",\"regex\":\"(?P<amount>\\\\d+)\",\"sample_text\":\"SO TIEN THAT 999\"}", 201)]
    [InlineData("DELETE", "11111111-2222-3333-4444-555555555555/samples/66666666-7777-8888-9999-000000000000", "sample.delete", null, 200)]
    public async Task EveryWrite_ForwardsTheAdminId_WritesOneAuditRow_AndNeverLogsTheBody(string method, string path, string action, string? body, int status)
    {
        var admin = await AdminTokenAsync();
        var before = (await FormatAuditLogsAsync(admin)).Count;
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(new HttpMethod(method), $"/api/v1/admin/provider-formats/{path}", admin, body));

        ((int)response.StatusCode).Should().Be(status);
        var call = _ai.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.FormatsArea);
        call.Method.Should().Be(new HttpMethod(method));
        call.Path.Should().Be(path);
        call.ActorId.Should().NotBeNull();
        var logs = await FormatAuditLogsAsync(admin);
        logs.Count.Should().Be(before + 1);
        logs.Select(l => l.Metadata).Should().Contain(m => m != null && m.Contains(action));
        logs.Select(l => l.Metadata).Should().NotContain(m => m != null && (m.Contains("SO TIEN THAT") || m.Contains("amount")));  // regex và mẫu không vào nhật ký chung
    }

    [Fact]
    public async Task AFailedWrite_KeepsTheAiServiceStatus_AndWritesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = (await FormatAuditLogsAsync(admin)).Count;
        _ai.Respond = _ => throw new AiRuleException(409, "format_not_draft", "Chỉ sửa được định dạng draft.");

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/v1/admin/provider-formats/{FormatId}/patterns", admin, """{"pattern_name":"p","regex":"x"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FormatAuditLogsAsync(admin)).Count.Should().Be(before);
        _ai.Respond = _ => Parse("""{"ok":true}""");
    }

    [Fact]
    public async Task RasaStatusAndParse_GoToTheRasaArea_AndLeaveNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        _ai.Calls.Clear();

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/rasa/status", admin))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/rasa/parse", admin, """{"text":"an pho 45k"}"""))).StatusCode.Should().Be(HttpStatusCode.OK);

        _ai.Calls.Select(c => (c.Area, c.Method, c.Path)).Should().Equal(
            (AiRulesRequest.RasaArea, HttpMethod.Get, "status"),
            (AiRulesRequest.RasaArea, HttpMethod.Post, "parse"));
        _ai.Calls.Should().OnlyContain(c => c.ActorId == null);
    }
}
