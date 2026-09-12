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

[Collection("Integration")]
public class AdminControllersTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AdminControllersTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record RegisteredUser(string Email, string AccessToken, string RefreshToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private record ProviderConfigData(
        Guid Id, string ProviderKey, string DisplayName, string PackageName, bool IsActive);
    private record ProviderConfigBody(bool Success, ProviderConfigData Data);
    private record ProviderConfigListBody(bool Success, List<ProviderConfigData> Data);

    private record CategoryData(Guid Id, string Name, string Slug, bool IsActive);
    private record CategoryBody(bool Success, CategoryData Data);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record UserCategoryData(Guid Id, string Name, string Slug, bool IsSystem);
    private record UserCategoryListBody(bool Success, List<UserCategoryData> Data);

    private record MissionData(
        Guid Id, string Code, string Title, int ConditionTarget, int ExpReward, bool IsActive);
    private record MissionBody(bool Success, MissionData Data);

    private record AdminUserData(
        Guid Id, string Email, string DisplayName, string Role, bool IsLocked, DateTimeOffset? DeletedAt);
    private record AdminUserListBody(bool Success, List<AdminUserData> Data);
    private record AdminUserBody(bool Success, AdminUserData Data);

    private record AiStatsProduction(int TotalAnalyzed, double? CategoryCorrectionRate);
    private record AiStatsData(AiStatsProduction Production, object? AiService, string? AiServiceError);
    private record AiStatsBody(bool Success, AiStatsData Data);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
    }

    private async Task<RegisteredUser> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Admin Test User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return new RegisteredUser(email, body!.Data.AccessToken, body.Data.RefreshToken);
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

    // ---------------------------------------------------------------- policy

    public static TheoryData<string, string> AdminEndpoints => new()
    {
        { "GET", "/api/v1/admin/users" },
        { "GET", "/api/v1/admin/provider-configs" },
        { "GET", "/api/v1/admin/categories" },
        { "GET", "/api/v1/admin/missions" },
        { "GET", "/api/v1/admin/ai-stats" },
        { "GET", "/api/v1/admin/audit-logs" },
    };

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task EveryAdminEndpointRejectsAnOrdinaryUser(string method, string url)
    {
        // Đây là test quan trọng nhất của Phase 8. Quên [Authorize(Policy = AdminOnly)] trên một
        // controller thì nó rơi về FallbackPolicy — bất kỳ ai đã đăng nhập cũng gọi được, và
        // response vẫn 200 nên không có gì báo động.
        var user = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Request(new HttpMethod(method), url, user.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task EveryAdminEndpointRejectsAnonymousRequests(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------- provider configs

    [Fact]
    public async Task ProviderConfigLifecycle()
    {
        var token = await AdminTokenAsync();
        var key = $"bank_{Guid.NewGuid():N}"[..20];
        var package = $"com.test.{Guid.NewGuid():N}";

        var created = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/provider-configs", token,
            new CreateProviderConfigRequest(key, "Ngân hàng thử", package, AccountType.Bank)));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var config = (await created.Content.ReadFromJsonAsync<ProviderConfigBody>(JsonOptions))!.Data;

        var duplicate = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/provider-configs", token,
            new CreateProviderConfigRequest(key, "Trùng key", $"com.other.{Guid.NewGuid():N}", AccountType.Bank)));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var renamed = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/provider-configs/{config.Id}", token,
            new UpdateProviderConfigRequest(null, "Tên mới", null, null)));
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<ProviderConfigBody>(JsonOptions))!.Data.DisplayName
            .Should().Be("Tên mới");

        var keyChange = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/provider-configs/{config.Id}", token,
            new UpdateProviderConfigRequest("doi_key", null, null, null)));
        keyChange.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await keyChange.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code
            .Should().Be("ADMIN_IMMUTABLE_FIELD");

        var deactivated = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/provider-configs/{config.Id}/activation", token,
            new SetActivationRequest(false)));
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/provider-configs", token));
        var activeList = await active.Content.ReadFromJsonAsync<ProviderConfigListBody>(JsonOptions);
        activeList!.Data.Should().NotContain(x => x.Id == config.Id);

        var all = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/provider-configs?includeInactive=true", token));
        var allList = await all.Content.ReadFromJsonAsync<ProviderConfigListBody>(JsonOptions);
        allList!.Data.Should().Contain(x => x.Id == config.Id && !x.IsActive);
    }

    // ---------------------------------------------------------- system categories

    [Fact]
    public async Task DeactivatingASystemCategoryHidesItFromUsersButKeepsItForAdmins()
    {
        var token = await AdminTokenAsync();
        var slug = $"thu_nghiem_{Guid.NewGuid():N}"[..24];

        var created = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/categories", token,
            new CreateSystemCategoryRequest("Danh mục thử", slug, "science")));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await created.Content.ReadFromJsonAsync<CategoryBody>(JsonOptions))!.Data;

        var user = await RegisterAndLoginAsync();
        var beforeResponse = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/categories", user.AccessToken));
        var before = await beforeResponse.Content.ReadFromJsonAsync<UserCategoryListBody>(JsonOptions);
        before!.Data.Should().Contain(x => x.Slug == slug);

        await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/categories/{category.Id}/activation", token,
            new SetActivationRequest(false)));

        var afterResponse = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/categories", user.AccessToken));
        var after = await afterResponse.Content.ReadFromJsonAsync<UserCategoryListBody>(JsonOptions);
        after!.Data.Should().NotContain(x => x.Slug == slug, "tắt danh mục là ngừng cho chọn mới");

        // Admin vẫn thấy nó — nó chưa bị xóa, chỉ là không còn chọn được.
        var adminView = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/categories?includeInactive=true", token));
        var adminList = await adminView.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        adminList!.Data.Should().Contain(x => x.Id == category.Id && !x.IsActive);
    }

    [Fact]
    public async Task DuplicateSystemCategorySlugIsRejected()
    {
        var token = await AdminTokenAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/categories", token,
            new CreateSystemCategoryRequest("Ăn uống lần hai", "food", null)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code
            .Should().Be("ADMIN_SYSTEM_CATEGORY_SLUG_DUPLICATE");
    }

    // -------------------------------------------------------------- missions

    [Fact]
    public async Task MissionLifecycle()
    {
        var token = await AdminTokenAsync();
        var code = $"thu_{Guid.NewGuid():N}"[..16];

        var created = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/admin/missions", token,
            new CreateMissionRequest(code, "Nhiệm vụ thử", "Mô tả",
                MissionPeriodType.Daily, MissionConditionType.ConfirmTransaction, 2, 20)));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var mission = (await created.Content.ReadFromJsonAsync<MissionBody>(JsonOptions))!.Data;

        var updated = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/missions/{mission.Id}", token,
            new UpdateMissionRequest(null, null, null, null, null, 5, 50)));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterUpdate = (await updated.Content.ReadFromJsonAsync<MissionBody>(JsonOptions))!.Data;
        afterUpdate.ConditionTarget.Should().Be(5);
        afterUpdate.ExpReward.Should().Be(50);

        var conditionChange = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/missions/{mission.Id}", token,
            new UpdateMissionRequest(null, null, null, null, MissionConditionType.ContributeToGoal, null, null)));
        conditionChange.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var deactivated = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/missions/{mission.Id}/activation", token,
            new SetActivationRequest(false)));
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivated.Content.ReadFromJsonAsync<MissionBody>(JsonOptions))!.Data.IsActive.Should().BeFalse();
    }

    // ----------------------------------------------------------------- users

    [Fact]
    public async Task UserListCarriesNoFinancialFields()
    {
        await RegisterAndLoginAsync();
        var token = await AdminTokenAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/users?limit=5", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đọc thô để bắt được cả trường mà DTO của test không khai báo — ARCHITECTURE.md §7.3
        // cấm admin đọc số tiền của người dùng cụ thể.
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny(
            "amountCents", "balanceCents", "totalSpent", "passwordHash", "refreshToken");
    }

    [Fact]
    public async Task LockingAUserStopsThemFromRefreshingTheirSession()
    {
        var user = await RegisterAndLoginAsync();
        var token = await AdminTokenAsync();

        // Tra theo email chứ không lấy "user chưa khoá đầu tiên": các test khác trong cùng
        // collection cũng đăng ký user, nên "đầu tiên" có thể là người khác và bài test sẽ
        // hỏng theo thứ tự chạy chứ không theo hành vi.
        var list = await _client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/users?limit=100&search={Uri.EscapeDataString(user.Email)}", token));
        var users = await list.Content.ReadFromJsonAsync<AdminUserListBody>(JsonOptions);
        var target = users!.Data.Single(u => u.Email == user.Email);

        var locked = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{target.Id}/lock", token,
            new SetUserLockRequest(true, "vi phạm điều khoản")));
        locked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await locked.Content.ReadFromJsonAsync<AdminUserBody>(JsonOptions))!.Data.IsLocked.Should().BeTrue();

        // Refresh token của CHÍNH user bị khóa phải hết tác dụng. Access token cũ vẫn sống tới
        // hết TTL — đó là biên trên đã biết của thiết kế JWT, không phải lỗi.
        var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(user.RefreshToken));
        refresh.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAdminCannotLockThemselves()
    {
        var token = await AdminTokenAsync();
        var list = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/users?role=Admin&limit=10", token));
        var users = await list.Content.ReadFromJsonAsync<AdminUserListBody>(JsonOptions);
        var admin = users!.Data.First(u => u.Email == AuthApiFactory.AdminEmail);

        var response = await _client.SendAsync(Request(HttpMethod.Patch, $"/api/v1/admin/users/{admin.Id}/lock", token,
            new SetUserLockRequest(true, null)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code
            .Should().Be("ADMIN_CANNOT_LOCK_SELF");
    }

    // -------------------------------------------------------------- ai-stats

    [Fact]
    public async Task AiStatsStillAnswersWhenTheAiServiceIsDown()
    {
        // FakeAIServiceClient luôn ném AIServiceUnavailableException — đúng tình huống AI
        // Service đang restart. Màn hình quản trị không được sập theo.
        var token = await AdminTokenAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/admin/ai-stats", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AiStatsBody>(JsonOptions);
        body!.Data.AiService.Should().BeNull();
        body.Data.AiServiceError.Should().NotBeNullOrEmpty();
        body.Data.Production.Should().NotBeNull();
    }
}
