using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Admin;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Hai lỗ hổng vận hành của phần quản trị người dùng:
/// đường duy nhất tạo admin là biến môi trường lúc khởi động, và hàng đợi xoá cứng chạy mà
/// không ai nhìn thấy cũng không có cách nào dừng.
/// </summary>
[Collection("Integration")]
public class AdminUserGovernanceControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AdminUserGovernanceControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record RegisteredUser(string Email, string Password, Guid Id, string AccessToken, string RefreshToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record AdminUserData(Guid Id, string Email, string Role, bool IsLocked, DateTimeOffset? DeletedAt);
    private record AdminUserBody(bool Success, AdminUserData Data);
    private record ProfileData(Guid Id, string Email);
    private record ProfileBody(bool Success, ProfileData Data);
    private record DeletionRequestData(
        Guid Id, Guid UserId, string Email, string Status, int DaysUntilHardDelete);
    private record DeletionRequestListBody(bool Success, List<DeletionRequestData> Data);
    private record DeletionRequestBody(bool Success, DeletionRequestData Data);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
    }

    private async Task<RegisteredUser> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Governance Test User"));

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);

        var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", body!.Data.AccessToken));
        var profile = await me.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions);

        return new RegisteredUser(
            email, password, profile!.Data.Id, body.Data.AccessToken, body.Data.RefreshToken);
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return request;
    }

    // ------------------------------------------------------------------ role

    [Fact]
    public async Task PromotingAUserLetsThemReachTheAdminApi()
    {
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        var before = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/users", user.AccessToken));
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var promote = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", adminToken,
            new SetUserRoleRequest(UserRole.Admin, "phụ trách vận hành")));
        promote.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đăng nhập lại để lấy access token mang vai trò mới.
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(user.Email, user.Password));
        var promoted = (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;

        var after = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/users", promoted));
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DemotingRevokesTheRefreshTokenSoTheOldSessionCannotRenew()
    {
        // Vai trò nằm trong access token; thu hồi refresh token là thứ kẹp cửa sổ leo thang
        // đặc quyền lại ở đúng TTL của access token.
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", adminToken,
            new SetUserRoleRequest(UserRole.Admin, null)));

        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(user.Email, user.Password));
        var session = (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data;

        await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", adminToken,
            new SetUserRoleRequest(UserRole.User, "hết nhiệm kỳ")));

        var refresh = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshRequest(session.RefreshToken));

        refresh.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "refresh token phải bị thu hồi khi hạ quyền");
    }

    [Fact]
    public async Task AnAdminCannotChangeTheirOwnRole()
    {
        var adminToken = await AdminTokenAsync();
        var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", adminToken));
        var adminId = (await me.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.Id;

        var response = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{adminId}/role", adminToken,
            new SetUserRoleRequest(UserRole.User, null)));

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("ADMIN_CANNOT_CHANGE_OWN_ROLE");
    }

    [Fact]
    public async Task TheLastActiveAdminCannotBeDemoted()
    {
        // Admin seed là admin hoạt động duy nhất trong DB test; một admin khác phải hạ được
        // người mới thăng, nhưng không hạ nổi người cuối cùng.
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", adminToken,
            new SetUserRoleRequest(UserRole.Admin, null)));

        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(user.Email, user.Password));
        var promoted = (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;

        var me = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", adminToken));
        var seedAdminId = (await me.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.Id;

        // Người vừa thăng hạ được admin seed (còn 2 admin nên vẫn hợp lệ)...
        var demoteSeed = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{seedAdminId}/role", promoted,
            new SetUserRoleRequest(UserRole.User, null)));
        demoteSeed.StatusCode.Should().Be(HttpStatusCode.OK);

        // ...nhưng giờ họ là admin cuối cùng, và admin seed đã bị hạ nên không hạ ngược lại được.
        var demoteLast = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", promoted,
            new SetUserRoleRequest(UserRole.User, null)));

        var body = await demoteLast.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("ADMIN_CANNOT_CHANGE_OWN_ROLE",
            "tự hạ quyền bị chặn trước, và đó cũng là cách duy nhất chạm tới admin cuối cùng");

        // Trả admin seed về nguyên trạng để không ảnh hưởng test khác trong cùng collection.
        await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{seedAdminId}/role", promoted,
            new SetUserRoleRequest(UserRole.Admin, "khôi phục sau test")));
    }

    // -------------------------------------------------------------- deletion

    [Fact]
    public async Task ADeletionRequestShowsUpInTheQueueAndCanBeCancelled()
    {
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        var delete = await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/auth/account", user.AccessToken,
            new DeleteAccountRequest(user.Password)));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Người dùng đã soft delete nên không tự huỷ được — đây chính là lý do phải có admin.
        var blocked = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(user.Email, user.Password));
        blocked.StatusCode.Should().NotBe(HttpStatusCode.OK);

        var list = await _client.SendAsync(Request(
            HttpMethod.Get, "/api/v1/admin/data-deletion-requests?status=Pending", adminToken));
        var items = (await list.Content.ReadFromJsonAsync<DeletionRequestListBody>(JsonOptions))!.Data;
        var mine = items.Should().ContainSingle(r => r.UserId == user.Id).Subject;
        mine.Email.Should().Be(user.Email, "không có email thì danh sách chỉ là một đống GUID");
        mine.DaysUntilHardDelete.Should().BeInRange(29, 30);

        var cancel = await _client.SendAsync(Request(
            HttpMethod.Post, $"/api/v1/admin/data-deletion-requests/{mine.Id}/cancel", adminToken,
            new CancelDataDeletionRequest("người dùng gọi lên đổi ý")));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancel.Content.ReadFromJsonAsync<DeletionRequestBody>(JsonOptions))!
            .Data.Status.Should().Be("Cancelled");

        // Bước quan trọng nhất: huỷ phải CỨU ĐƯỢC người dùng, không chỉ dọn hàng đợi.
        var restored = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(user.Email, user.Password));
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CancellingTwiceStaysCancelled()
    {
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/auth/account", user.AccessToken,
            new DeleteAccountRequest(user.Password)));

        var list = await _client.SendAsync(Request(
            HttpMethod.Get, "/api/v1/admin/data-deletion-requests?status=Pending", adminToken));
        var mine = (await list.Content.ReadFromJsonAsync<DeletionRequestListBody>(JsonOptions))!
            .Data.Single(r => r.UserId == user.Id);

        var url = $"/api/v1/admin/data-deletion-requests/{mine.Id}/cancel";
        await _client.SendAsync(Request(HttpMethod.Post, url, adminToken, new CancelDataDeletionRequest(null)));
        var second = await _client.SendAsync(Request(
            HttpMethod.Post, url, adminToken, new CancelDataDeletionRequest(null)));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<DeletionRequestBody>(JsonOptions))!
            .Data.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task PromotingAnAccountAwaitingDeletionIsRefused()
    {
        var adminToken = await AdminTokenAsync();
        var user = await RegisterAndLoginAsync();

        await _client.SendAsync(Request(
            HttpMethod.Delete, "/api/v1/auth/account", user.AccessToken,
            new DeleteAccountRequest(user.Password)));

        var response = await _client.SendAsync(Request(
            HttpMethod.Patch, $"/api/v1/admin/users/{user.Id}/role", adminToken,
            new SetUserRoleRequest(UserRole.Admin, null)));

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
