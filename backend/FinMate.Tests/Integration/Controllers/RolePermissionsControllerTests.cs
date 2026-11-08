using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Admin chỉnh quyền của Staff/OpsManager. Điều đáng kiểm là HIỆU LỰC thật: cấp quyền thì API chặn trước đó mở ra, bỏ quyền thì API đang mở bị chặn,
/// chứ không chỉ là bảng ma trận lưu được. Mỗi test trả quyền về mặc định để không làm lệch test khác dùng chung DB.
/// </summary>
[Collection("Integration")]
public class RolePermissionsControllerTests : IClassFixture<AuthApiFactory>
{
    private const string Endpoint = "/api/v1/admin/role-permissions";
    private const string Password = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public RolePermissionsControllerTests(AuthApiFactory factory)
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
    private record RoleData(string Role, bool Editable, List<string> Capabilities);
    private record ListData(List<string> Catalog, List<string> AdminOnly, List<RoleData> Roles);
    private record ListBody(ListData Data);
    private record Account(string Email, Guid Id, string AccessToken);

    private async Task<TokenData> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data;
    }

    private async Task<string> AdminTokenAsync() => (await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword)).AccessToken;

    private async Task<Account> AccountWithRoleAsync(UserRole role)
    {
        var admin = await AdminTokenAsync();
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Perm Test"));
        var first = await LoginAsync(email, Password);
        var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", first.AccessToken));
        var id = (await me.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
        (await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{id}/role", admin, new SetUserRoleRequest(role, "kiểm thử")))).StatusCode.Should().Be(HttpStatusCode.OK);
        return new Account(email, id, (await LoginAsync(email, Password)).AccessToken);
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

    private Task<HttpResponseMessage> SetAsync(string adminToken, UserRole role, IEnumerable<string> capabilities, string? reason = "kiểm thử")
        => _client.SendAsync(Request(HttpMethod.Put, $"{Endpoint}/{role}", adminToken, new SaveRolePermissionsRequest(capabilities.ToList(), reason)));

    private async Task<HttpStatusCode> StatusAsync(string url, string token)
        => (await _client.SendAsync(Request(HttpMethod.Get, url, token))).StatusCode;

    private static IEnumerable<string> Defaults(UserRole role) => OpsPermissions.Names(OpsPermissions.For(role));

    [Fact]
    public async Task ListShowsTheCatalogAndWhoIsEditable()
    {
        var admin = await AdminTokenAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Get, Endpoint, admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data;
        data.Catalog.Should().Contain(["HandleTickets", "ViewMonitoring", "ManageSystem"]);
        data.AdminOnly.Should().BeEquivalentTo("ManageSystem");
        data.Roles.Single(r => r.Role == "Staff").Editable.Should().BeTrue();
        data.Roles.Single(r => r.Role == "OpsManager").Editable.Should().BeTrue();
        data.Roles.Single(r => r.Role == "Admin").Editable.Should().BeFalse();
        data.Roles.Single(r => r.Role == "User").Capabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantingAnExtraCapabilityOpensTheEndpointItGuards()
    {
        var admin = await AdminTokenAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);
        (await StatusAsync("/api/v1/ops/monitoring/status", staff.AccessToken)).Should().Be(HttpStatusCode.Forbidden);

        try
        {
            (await SetAsync(admin, UserRole.Staff, Defaults(UserRole.Staff).Append("ViewMonitoring"))).StatusCode.Should().Be(HttpStatusCode.OK);

            (await StatusAsync("/api/v1/ops/monitoring/status", staff.AccessToken)).Should().Be(HttpStatusCode.OK);
            var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/ops/me", staff.AccessToken));
            (await me.Content.ReadAsStringAsync()).Should().Contain("ViewMonitoring");
        }
        finally
        {
            await SetAsync(admin, UserRole.Staff, Defaults(UserRole.Staff));
        }

        (await StatusAsync("/api/v1/ops/monitoring/status", staff.AccessToken)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokingACapabilityClosesTheEndpointImmediatelyWithTheSameToken()
    {
        var admin = await AdminTokenAsync();
        var manager = await AccountWithRoleAsync(UserRole.OpsManager);
        (await StatusAsync("/api/v1/ops/dashboard", manager.AccessToken)).Should().Be(HttpStatusCode.OK);

        try
        {
            (await SetAsync(admin, UserRole.OpsManager, Defaults(UserRole.OpsManager).Where(c => c != "ViewDashboard"))).StatusCode.Should().Be(HttpStatusCode.OK);

            // Cùng access token (vai trò vẫn là OpsManager trong JWT): quyền thật đọc từ DB nên bị chặn ngay.
            (await StatusAsync("/api/v1/ops/dashboard", manager.AccessToken)).Should().Be(HttpStatusCode.Forbidden);
            (await StatusAsync("/api/v1/ops/tickets", manager.AccessToken)).Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetAsync(admin, UserRole.OpsManager, Defaults(UserRole.OpsManager));
        }

        (await StatusAsync("/api/v1/ops/dashboard", manager.AccessToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AStaffWithNoCapabilityIsShutOutOfTheQueue()
    {
        var admin = await AdminTokenAsync();
        var staff = await AccountWithRoleAsync(UserRole.Staff);

        try
        {
            (await SetAsync(admin, UserRole.Staff, [])).StatusCode.Should().Be(HttpStatusCode.OK);

            (await StatusAsync("/api/v1/ops/tickets", staff.AccessToken)).Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SetAsync(admin, UserRole.Staff, Defaults(UserRole.Staff));
        }
    }

    [Theory]
    [InlineData("Admin", new[] { "HandleTickets" }, "ROLE_PERMISSIONS_ROLE_NOT_EDITABLE")]
    [InlineData("User", new[] { "HandleTickets" }, "ROLE_PERMISSIONS_ROLE_NOT_EDITABLE")]
    [InlineData("Staff", new[] { "HandleTickets", "ManageSystem" }, "ROLE_PERMISSIONS_ADMIN_ONLY")]
    [InlineData("Staff", new[] { "NoSuchCapability" }, "ROLE_PERMISSIONS_UNKNOWN_CAPABILITY")]
    [InlineData("Staff", new[] { "AccessCustomerData" }, "ROLE_PERMISSIONS_BROKEN_DEPENDENCY")]
    [InlineData("Staff", new[] { "HandleTickets", "CreateDraftTransactions" }, "ROLE_PERMISSIONS_BROKEN_DEPENDENCY")]
    public async Task RefusesChangesThatBreakTheRules(string role, string[] capabilities, string code)
    {
        var response = await SetAsync(await AdminTokenAsync(), Enum.Parse<UserRole>(role), capabilities);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
    }

    [Fact]
    public async Task RequiresAReasonToChangeAnything()
    {
        var response = await SetAsync(await AdminTokenAsync(), UserRole.Staff, Defaults(UserRole.Staff), reason: "  ");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnlyAnAdminCanChangeOrReadRolePermissions()
    {
        var staff = await AccountWithRoleAsync(UserRole.Staff);

        (await StatusAsync(Endpoint, staff.AccessToken)).Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Request(HttpMethod.Put, $"{Endpoint}/Staff", staff.AccessToken, new SaveRolePermissionsRequest(["ViewMonitoring"], "tự cấp")))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync(Endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
