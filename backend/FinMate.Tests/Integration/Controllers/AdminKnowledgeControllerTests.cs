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
/// Kho tri thức của agent: chỉ admin, chuyển tiếp tới khu vực <c>knowledge</c> của AI Service (client giả ghi lại lời gọi). Gửi / duyệt / gỡ thành công để lại đúng một dòng audit_logs chỉ có mã tài liệu
/// (không nội dung); đọc, hoặc bị AI Service từ chối, thì không để lại dòng nào.
/// </summary>
[Collection("Integration")]
public class AdminKnowledgeControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Base = "/api/v1/admin/knowledge";
    private static readonly Guid DocId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private const string SecretText = "Nội dung tài liệu không được lọt vào nhật ký kiểm tra";

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

    public AdminKnowledgeControllerTests(AuthApiFactory factory)
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Knowledge Test User"));
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

    private async Task<int> KnowledgeAuditCountAsync(string adminToken)
        => (await AuditAsync(adminToken, AuditEvents.AdminKnowledgeSubmitted)).Count
           + (await AuditAsync(adminToken, AuditEvents.AdminKnowledgeApproved)).Count
           + (await AuditAsync(adminToken, AuditEvents.AdminKnowledgeRetired)).Count;

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnEveryRoute()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, $"{Base}/documents", null),
                     (HttpMethod.Get, $"{Base}/documents/{DocId}", null),
                     (HttpMethod.Post, $"{Base}/documents", """{"title":"t"}"""),
                     (HttpMethod.Post, $"{Base}/documents/{DocId}/approve", null),
                     (HttpMethod.Post, $"{Base}/documents/{DocId}/retire", null),
                     (HttpMethod.Get, $"{Base}/candidates", null),
                     (HttpMethod.Post, $"{Base}/candidates/{DocId}/resolve", """{"status":"Covered"}"""),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _ai.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GettingOneDocument_AsksTheAiServiceForItsFullText_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = await KnowledgeAuditCountAsync(admin);
        _ai.Calls.Clear();
        _ai.Respond = _ => Parse("""{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","title":"Quy tắc 50/30/20","status":"Draft","text":"Nội dung đầy đủ"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"{Base}/documents/{DocId}", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Nội dung đầy đủ");
        _ai.Calls.Single().Should().Match<AiRulesRequest>(c => c.Path == $"documents/{DocId}" && c.Method == HttpMethod.Get && c.Area == AiRulesRequest.KnowledgeArea);
        (await KnowledgeAuditCountAsync(admin)).Should().Be(before);
    }

    [Fact]
    public async Task Listing_PassesTheServerSideQueryThrough_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = await KnowledgeAuditCountAsync(admin);
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Request(HttpMethod.Get, $"{Base}/documents?status=Approved&search=phong&limit=5&offset=10", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.KnowledgeArea);
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be("documents");
        call.Query.Should().Equal(new Dictionary<string, string?> { ["status"] = "Approved", ["search"] = "phong", ["limit"] = "5", ["offset"] = "10" });
        (await KnowledgeAuditCountAsync(admin)).Should().Be(before);
    }

    [Fact]
    public async Task Submitting_ForwardsTheBodyWithTheAdminAsActor_AndAuditsTheIdWithoutTheContent()
    {
        var admin = await AdminTokenAsync();
        var before = (await AuditAsync(admin, AuditEvents.AdminKnowledgeSubmitted)).Count;
        _ai.Calls.Clear();
        _ai.Respond = _ => Parse($$"""{"id":"{{DocId}}","status":"Draft","chunks":4}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/documents", admin, $$"""{"title":"Phong bì","source_name":"Sổ tay","as_of":"2026-11-01T00:00:00Z","effective_from":"2026-11-01T00:00:00Z","text":"{{SecretText}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var call = _ai.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("documents");
        call.ActorId.Should().NotBeNull();
        call.Body!.Value.GetProperty("source_name").GetString().Should().Be("Sổ tay");
        var rows = await AuditAsync(admin, AuditEvents.AdminKnowledgeSubmitted);
        rows.Should().HaveCount(before + 1);
        rows.OrderByDescending(r => r.CreatedAt).First().Metadata.Should().Contain(DocId.ToString()).And.NotContain(SecretText);
    }

    [Theory]
    [InlineData("approve", AuditEvents.AdminKnowledgeApproved)]
    [InlineData("retire", AuditEvents.AdminKnowledgeRetired)]
    public async Task ApprovingOrRetiring_GoesToTheDocumentRoute_AndWritesOneAuditRowWithTheDocumentId(string action, string auditEvent)
    {
        var admin = await AdminTokenAsync();
        var before = (await AuditAsync(admin, auditEvent)).Count;
        _ai.Calls.Clear();
        _ai.Respond = _ => Parse($$"""{"id":"{{DocId}}","status":"x"}""");

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/documents/{DocId}/{action}", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _ai.Calls.Single().Should().Match<AiRulesRequest>(c => c.Path == $"documents/{DocId}/{action}" && c.Area == AiRulesRequest.KnowledgeArea && c.ActorId != null);
        var rows = await AuditAsync(admin, auditEvent);
        rows.Should().HaveCount(before + 1);
        rows.OrderByDescending(r => r.CreatedAt).First().Metadata.Should().Contain(DocId.ToString());
    }

    [Fact]
    public async Task ARejectedTransition_KeepsTheAiServiceStatus_AndLeavesNoAuditRow()
    {
        var admin = await AdminTokenAsync();
        var before = await KnowledgeAuditCountAsync(admin);
        _ai.Respond = _ => throw new AiRuleException(409, "kb_invalid_transition", "Không chuyển được trạng thái.");

        var response = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/documents/{DocId}/approve", admin));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await KnowledgeAuditCountAsync(admin)).Should().Be(before);
    }

    [Fact]
    public async Task Candidates_AreListedWithTheQueryAndResolvedWithTheBody_WithoutCreatingAnyDocument()
    {
        var admin = await AdminTokenAsync();
        _ai.Calls.Clear();

        await _client.SendAsync(Request(HttpMethod.Get, $"{Base}/candidates?status=New&limit=20", admin));
        await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/candidates/{DocId}/resolve", admin, """{"status":"Covered"}"""));

        _ai.Calls.Should().HaveCount(2);
        _ai.Calls[0].Should().Match<AiRulesRequest>(c => c.Path == "candidates" && c.Method == HttpMethod.Get);
        _ai.Calls[1].Should().Match<AiRulesRequest>(c => c.Path == $"candidates/{DocId}/resolve" && c.Method == HttpMethod.Post);
        _ai.Calls.Should().NotContain(c => c.Path == "documents"); // ứng viên không bao giờ tự thành tài liệu
    }
}
