using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Configuration;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Hai vai trò vận hành mới không được chạm vào API admin, và logic đổi vai trò không còn giả định hệ thống chỉ có User và Admin.</summary>
[Collection("Integration")]
public class OpsRolesControllerTests : IClassFixture<AuthApiFactory>
{
    private const string OpsMe = "/api/v1/ops/me";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] AdminEndpoints =
    [
        "/api/v1/admin/users",
        "/api/v1/admin/audit-logs",
        "/api/v1/admin/ai-stats",
        "/api/v1/admin/categories",
        "/api/v1/admin/data-deletion-requests",
        "/api/v1/admin/provider-configs",
        "/api/v1/admin/provider-formats",
        "/api/v1/admin/secrets",
        "/api/v1/admin/system-config",
        "/api/v1/admin/finance-methods",
        "/api/v1/admin/finance-method-prompts",
        "/api/v1/admin/goal-templates",
        "/api/v1/admin/survey-questions",
        "/api/v1/admin/ai-rules/meta",
        "/api/v1/admin/ai-config/documents",
    ];

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public OpsRolesControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record TokenData(string AccessToken, string RefreshToken);
    private record TokenBody(TokenData Data);
    private record IdData(Guid Id);
    private record IdBody(IdData Data);
    private record ErrorData(string Code);
    private record ErrorBody(ErrorData Error);
    private record OpsMeData(Guid Id, string Role, List<string> Capabilities);
    private record OpsMeBody(OpsMeData Data);
    private record Account(string Email, Guid Id, string AccessToken, string RefreshToken);

    private const string Password = "Password123!";

    private async Task<TokenData> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data;
    }

    private Task<TokenData> AdminAsync() => LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

    private async Task<Account> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Ops Test"));
        var tokens = await LoginAsync(email, Password);
        var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", tokens.AccessToken));
        var id = (await me.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
        return new Account(email, id, tokens.AccessToken, tokens.RefreshToken);
    }

    private Task<HttpResponseMessage> SetRoleAsync(string adminToken, Guid userId, UserRole role)
        => _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{userId}/role", adminToken, new SetUserRoleRequest(role, "kiểm thử")));

    /// <summary>Tài khoản đã mang vai trò trong token.</summary>
    private async Task<Account> AccountWithRoleAsync(UserRole role)
    {
        var admin = await AdminAsync();
        var account = await RegisterAsync();
        (await SetRoleAsync(admin.AccessToken, account.Id, role)).StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await LoginAsync(account.Email, Password);
        return account with { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken };
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return request;
    }

    [Fact]
    public async Task AnonymousAndOrdinaryUsersCannotReachTheOpsApi()
    {
        (await _client.GetAsync(OpsMe)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var user = await RegisterAsync();
        (await _client.SendAsync(Request(HttpMethod.Get, OpsMe, user.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Staff, "HandleTickets", "DecideApprovals")]
    [InlineData(UserRole.OpsManager, "DecideApprovals", "ManageSystem")]
    public async Task OpsMeReturnsTheRoleAndItsCapabilities(UserRole role, string has, string lacks)
    {
        var account = await AccountWithRoleAsync(role);

        var response = await _client.SendAsync(Request(HttpMethod.Get, OpsMe, account.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = (await response.Content.ReadFromJsonAsync<OpsMeBody>(JsonOptions))!.Data;
        me.Id.Should().Be(account.Id);
        me.Role.Should().Be(role.ToString());
        me.Capabilities.Should().Contain(has).And.NotContain(lacks);
    }

    [Fact]
    public async Task AdminAlsoWorksAsOpsWithEveryCapability()
    {
        var admin = await AdminAsync();
        var response = await _client.SendAsync(Request(HttpMethod.Get, OpsMe, admin.AccessToken));
        var me = (await response.Content.ReadFromJsonAsync<OpsMeBody>(JsonOptions))!.Data;
        me.Capabilities.Should().Contain(["HandleTickets", "DecideApprovals", "ManageSystem"]);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.OpsManager)]
    public async Task OpsRolesAreRefusedOnEveryAdminControllerGroup(UserRole role)
    {
        var account = await AccountWithRoleAsync(role);

        foreach (var endpoint in AdminEndpoints)
        {
            var response = await _client.SendAsync(Request(HttpMethod.Get, endpoint, account.AccessToken));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, endpoint);
        }
    }

    [Fact]
    public async Task OpsRoleCannotGrantItselfOrOthersARole()
    {
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        var target = await RegisterAsync();

        (await SetRoleAsync(manager.AccessToken, target.Id, UserRole.Staff)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SetRoleAsync(manager.AccessToken, manager.Id, UserRole.Admin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DemotedStaffLosesOpsAccessImmediatelyEvenWithAStillValidToken()
    {
        var admin = await AdminAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);
        (await _client.SendAsync(Request(HttpMethod.Get, OpsMe, staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SetRoleAsync(admin.AccessToken, staff.Id, UserRole.User)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.SendAsync(Request(HttpMethod.Get, OpsMe, staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LockedStaffLosesOpsAccessImmediately()
    {
        var admin = await AdminAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);

        var locked = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{staff.Id}/lock", admin.AccessToken, new SetUserLockRequest(true, "kiểm thử")));
        locked.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.SendAsync(Request(HttpMethod.Get, OpsMe, staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin, UserRole.OpsManager)]
    [InlineData(UserRole.Admin, UserRole.Staff)]
    [InlineData(UserRole.OpsManager, UserRole.Staff)]
    [InlineData(UserRole.Staff, UserRole.User)]
    public async Task EveryReductionOfPrivilegeRevokesTheRefreshToken(UserRole from, UserRole to)
    {
        var admin = await AdminAsync();
        var account = await AccountWithRoleAsync(from);

        (await SetRoleAsync(admin.AccessToken, account.Id, to)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(account.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PromotionKeepsTheSessionAndTheNextRefreshCarriesTheNewRole()
    {
        var admin = await AdminAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);

        (await SetRoleAsync(admin.AccessToken, staff.Id, UserRole.OpsManager)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(staff.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await refresh.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data.AccessToken;

        var me = await _client.SendAsync(Request(HttpMethod.Get, OpsMe, token));
        (await me.Content.ReadFromJsonAsync<OpsMeBody>(JsonOptions))!.Data.Role.Should().Be(nameof(UserRole.OpsManager));
    }

    [Fact]
    public async Task AccountAwaitingDeletionCannotBeGivenAnInternalRole()
    {
        var admin = await AdminAsync();
        var user = await RegisterAsync();
        var deleted = await _client.SendAsync(Request(HttpMethod.Delete, "/api/v1/auth/account", user.AccessToken, new { password = Password }));
        deleted.IsSuccessStatusCode.Should().BeTrue();

        (await SetRoleAsync(admin.AccessToken, user.Id, UserRole.Staff)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task DuringMaintenanceStaffOnlyReachesTheOpsApi()
    {
        var admin = (await AdminAsync()).AccessToken;
        var staff = await AccountWithRoleAsync(UserRole.Staff);
        var config = "/api/v1/admin/system-config";

        async Task SaveMaintenanceAsync(bool enabled)
        {
            var current = await _client.SendAsync(Request(HttpMethod.Get, config, admin));
            var version = JsonDocument.Parse(await current.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("version").GetInt32();
            var body = JsonSerializer.Serialize(new { expectedVersion = version, content = new { maintenance = new { enabled } }, note = "kiem thu" });
            var request = new HttpRequestMessage(HttpMethod.Put, config) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await SaveMaintenanceAsync(true);
        try
        {
            (await _client.SendAsync(Request(HttpMethod.Get, OpsMe, staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", staff.AccessToken))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await SaveMaintenanceAsync(false);
        }
    }

    [Fact]
    public async Task PreflightFromAnAllowedOriginPassesAndOtherOriginsGetNoCorsHeaders()
    {
        const string allowed = "https://ops.finmate.test";
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<CorsOptions>(options => options.AddPolicy(
                OpsWebOrigins.PolicyName,
                policy => policy.WithOrigins(allowed).AllowAnyMethod().WithHeaders("Authorization", "Content-Type"))))).CreateClient();

        async Task<HttpResponseMessage> PreflightAsync(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, OpsMe);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            return await client.SendAsync(request);
        }

        var ok = await PreflightAsync(allowed);
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ok.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(allowed);

        var other = await PreflightAsync("https://evil.test");
        other.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task ByDefaultNoBrowserOriginIsAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, OpsMe);
        request.Headers.Add("Origin", "https://ops.finmate.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
