using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Ma trận quyền của nhân sự (docs/rbac-matrix.md): ContentStaff soạn nhưng không duyệt, Manager duyệt, SupportStaff không vào khu quản trị người dùng, và một giá trị vai trò lạ trong JWT bị từ chối.
/// Mỗi hành động có quyền cao hơn controller của nó được khóa riêng ở đây vì thiếu một attribute sẽ không báo lỗi gì.
/// </summary>
[Collection("Integration")]
public class RbacTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid SomeId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private sealed class FakeAiRules : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            using var document = JsonDocument.Parse("""{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","status":"Approved","chunks":1}""");
            return Task.FromResult<JsonElement?>(document.RootElement.Clone());
        }
    }

    private readonly FakeAiRules _ai = new();
    private readonly HttpClient _client;

    public RbacTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_ai);
        })).CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record ProfileData(Guid Id);
    private record ProfileBody(ProfileData Data);
    private record Staff(Guid Id, string Email, string AccessToken, string RefreshToken);

    private static HttpRequestMessage Req(HttpMethod method, string url, string? token, string? json = null)
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

    private async Task<AuthResultData> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data;
    }

    private async Task<string> AdminTokenAsync() => (await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword)).AccessToken;

    /// <summary>Đăng ký một người dùng rồi (nếu có) admin cấp vai trò; trả token ĐĂNG NHẬP LẠI sau khi đổi vai trò, vì vai trò nằm trong access token.</summary>
    private async Task<Staff> UserWithRoleAsync(UserRole role)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Rbac Test User"));
        var first = await LoginAsync(email, "Password123!");
        var id = (await (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/users/me", first.AccessToken))).Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.Id;
        if (role != UserRole.User)
        {
            var changed = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{id}/role", await AdminTokenAsync(), JsonSerializer.Serialize(new { role = role.ToString(), reason = "test" })));
            changed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var session = await LoginAsync(email, "Password123!");
        return new Staff(id, email, session.AccessToken, session.RefreshToken);
    }

    private static string DraftAssumption(string key) => $$"""{"key":"{{key}}","rateBps":500,"unit":"pct_per_year","sourceName":"Tổng cục Thống kê","sourceUrl":"https://example.org/{{key}}","asOf":"2026-11-01T00:00:00+07:00"}""";

    private record AssumptionData(Guid Id);
    private record AssumptionBody(AssumptionData Data);

    private async Task<Guid> CreateDraftAsync(string token, string key)
    {
        var created = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/assumptions", token, DraftAssumption(key)));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<AssumptionBody>(JsonOptions))!.Data.Id;
    }

    [Fact]
    public async Task content_staff_cannot_approve_assumption()
    {
        var content = await UserWithRoleAsync(UserRole.ContentStaff);
        var draft = await CreateDraftAsync(content.AccessToken, $"rbac_a_{Guid.NewGuid():N}"[..30]); // soạn bản nháp thì được

        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/assumptions/{draft}/approve", content.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/assumptions/{draft}/retire", content.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task manager_can_approve()
    {
        var content = await UserWithRoleAsync(UserRole.ContentStaff);
        var manager = await UserWithRoleAsync(UserRole.Manager);
        var draft = await CreateDraftAsync(content.AccessToken, $"rbac_m_{Guid.NewGuid():N}"[..30]);

        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/assumptions/{draft}/approve", manager.AccessToken))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task approving_knowledge_needs_a_manager_but_drafting_and_listing_need_only_content_staff()
    {
        var content = await UserWithRoleAsync(UserRole.ContentStaff);
        var manager = await UserWithRoleAsync(UserRole.Manager);
        _ai.Calls.Clear();

        (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/knowledge/documents", content.AccessToken))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/knowledge/documents", content.AccessToken, "{}"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var callsBeforeApprove = _ai.Calls.Count;

        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/knowledge/documents/{SomeId}/approve", content.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/knowledge/documents/{SomeId}/retire", content.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _ai.Calls.Should().HaveCount(callsBeforeApprove); // bị chặn trước khi chạm tới AI Service

        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/knowledge/documents/{SomeId}/approve", manager.AccessToken))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(UserRole.SupportStaff)]
    [InlineData(UserRole.ContentStaff)]
    [InlineData(UserRole.Manager)]
    public async Task only_an_admin_opens_the_admin_users_api(UserRole role)
    {
        var staff = await UserWithRoleAsync(role);

        (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/users", staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/users", await AdminTokenAsync()))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task support_staff_cannot_open_admin_users_or_the_content_areas()
    {
        var support = await UserWithRoleAsync(UserRole.SupportStaff);

        foreach (var url in new[] { "/api/v1/admin/users", "/api/v1/admin/assumptions", "/api/v1/admin/knowledge/documents", "/api/v1/admin/audit-logs" })
        {
            (await _client.SendAsync(Req(HttpMethod.Get, url, support.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }
    }

    [Fact]
    public async Task a_plain_user_and_an_anonymous_caller_reach_none_of_it()
    {
        var user = await UserWithRoleAsync(UserRole.User);

        foreach (var url in new[] { "/api/v1/admin/assumptions", "/api/v1/admin/knowledge/documents" })
        {
            (await _client.SendAsync(Req(HttpMethod.Get, url, user.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await _client.SendAsync(Req(HttpMethod.Get, url, null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Theory]
    [InlineData("Superuser")]
    [InlineData("admin")] // sai chữ hoa/thường: vai trò so khớp đúng từng ký tự
    [InlineData("Admin,Manager")]
    [InlineData("")]
    public async Task unknown_role_value_in_jwt_is_denied(string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Email, "x@finmate.local") };
        if (role.Length > 0)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 64))); // JWT_SECRET của AuthApiFactory
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        foreach (var url in new[] { "/api/v1/admin/users", "/api/v1/admin/assumptions", "/api/v1/admin/knowledge/documents" })
        {
            (await _client.SendAsync(Req(HttpMethod.Get, url, token))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} → {url}");
        }
    }

    [Fact]
    public async Task role_change_revokes_refresh_tokens_when_the_new_role_does_not_cover_the_old_one()
    {
        var admin = await AdminTokenAsync();
        var manager = await UserWithRoleAsync(UserRole.Manager);

        // Manager → ContentStaff là HẠ quyền (mất khả năng duyệt): refresh token cũ phải chết
        (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{manager.Id}/role", admin, """{"role":"ContentStaff","reason":"đổi nhiệm vụ"}"""))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(manager.RefreshToken))).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task a_lateral_move_between_staff_groups_also_revokes_but_a_promotion_does_not()
    {
        var admin = await AdminTokenAsync();
        var content = await UserWithRoleAsync(UserRole.ContentStaff);
        var plain = await UserWithRoleAsync(UserRole.User);

        await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{content.Id}/role", admin, """{"role":"SupportStaff"}"""));
        await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{plain.Id}/role", admin, """{"role":"Manager"}"""));

        (await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(content.RefreshToken))).StatusCode.Should().NotBe(HttpStatusCode.OK); // mất quyền nội dung
        (await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(plain.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.OK); // thăng quyền: không đá ra
    }

    [Fact]
    public async Task nobody_changes_their_own_role_even_to_a_lower_staff_role()
    {
        var promoted = await UserWithRoleAsync(UserRole.Admin);

        var self = await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{promoted.Id}/role", promoted.AccessToken, """{"role":"Manager"}"""));

        self.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
