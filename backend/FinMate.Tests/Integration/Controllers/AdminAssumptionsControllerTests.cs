using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Giả định kinh tế cho agent: chỉ admin, phải có nguồn, đi qua Draft → Approved, mỗi lần ghi để lại một dòng audit_logs.</summary>
[Collection("Integration")]
public class AdminAssumptionsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Base = "/api/v1/admin/assumptions";
    private readonly HttpClient _client;

    public AdminAssumptionsControllerTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AssumptionBody(Guid Id, string Key, int RateBps, string SourceName, string SourceUrl, int Version, string Status, Guid? ApprovedBy);
    private record OneBody(bool Success, AssumptionBody Data);
    private record ListBody(bool Success, List<AssumptionBody> Data);
    private record ErrorData(string Code);
    private record ErrorBody(bool Success, ErrorData Error);
    private record AuditLogData(string EventType);
    private record AuditLogListBody(List<AuditLogData> Data);

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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Assumption Test User"));
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

    private static string CreateJson(string key, int bps = 500, string sourceUrl = "https://www.gso.gov.vn/")
        => $$"""{"key":"{{key}}","rateBps":{{bps}},"unit":"pct_per_year","sourceName":"Tổng cục Thống kê","sourceUrl":"{{sourceUrl}}","asOf":"2026-11-01T00:00:00+07:00"}""";

    private async Task<AssumptionBody> CreateAsync(string admin, string key, int bps = 500)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, Base, admin, CreateJson(key, bps)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OneBody>(JsonOptions))!.Data;
    }

    private async Task<int> AuditCountAsync(string admin, string eventType)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={eventType}&limit=100", admin));
        return (await response.Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data.Count;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnEveryRoute()
    {
        var user = await UserTokenAsync();
        foreach (var (method, url, json) in new[]
                 {
                     (HttpMethod.Get, Base, null),
                     (HttpMethod.Post, Base, CreateJson("car_price_growth")),
                     (HttpMethod.Post, $"{Base}/{Guid.NewGuid()}/approve", null),
                     (HttpMethod.Post, $"{Base}/{Guid.NewGuid()}/retire", null),
                 })
        {
            (await _client.SendAsync(Request(method, url, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Request(method, url, user, json))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Draft_IsInvisibleUntilApproved_ThenApprovalRetiresThePreviousVersion_AndIsAudited()
    {
        var admin = await AdminTokenAsync();
        var key = $"car_{Guid.NewGuid():N}"[..20];
        var auditBefore = await AuditCountAsync(admin, "Admin.Assumption.Approved");

        var v1 = await CreateAsync(admin, key, 500);
        v1.Status.Should().Be("draft");
        v1.Version.Should().Be(1);
        (await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/{v1.Id}/approve", admin))).StatusCode.Should().Be(HttpStatusCode.OK);

        var v2 = await CreateAsync(admin, key, 700);
        v2.Version.Should().Be(2);
        var approved = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/{v2.Id}/approve", admin));
        (await approved.Content.ReadFromJsonAsync<OneBody>(JsonOptions))!.Data.ApprovedBy.Should().NotBeNull();

        var list = (await (await _client.SendAsync(Request(HttpMethod.Get, $"{Base}?key={key}", admin))).Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data;
        list.Select(a => (a.Version, a.Status)).Should().BeEquivalentTo([(2, "approved"), (1, "retired")]);
        (await AuditCountAsync(admin, "Admin.Assumption.Approved")).Should().Be(auditBefore + 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gso.gov.vn")]
    public async Task ASourceThatIsNotAnHttpUrl_Is422(string sourceUrl)
    {
        var admin = await AdminTokenAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Post, Base, admin, CreateJson("house_price_growth", 600, sourceUrl)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("assumption_source_required");
    }

    [Fact]
    public async Task ApprovingTwice_Is409_AndAnUnknownId_Is404()
    {
        var admin = await AdminTokenAsync();
        var draft = await CreateAsync(admin, $"gold_{Guid.NewGuid():N}"[..20]);
        (await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/{draft.Id}/approve", admin))).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/{draft.Id}/approve", admin));

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.SendAsync(Request(HttpMethod.Post, $"{Base}/{Guid.NewGuid()}/approve", admin))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
