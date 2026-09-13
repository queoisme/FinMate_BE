using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Bốn chỗ code lệch đặc tả ở Core Flow 1 &amp; 2, phát hiện khi đọc lại docx đầy đủ:
/// danh sách provider cho onboarding (Bước 1.2), thu nhập hằng tháng (Bước 1.3), tab
/// "Chờ duyệt" (Bước 5.4), và cảnh báo ngân sách tức thì (Flow 2 mục 2a).
/// </summary>
[Collection("Integration")]
public class CoreFlowGapsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public CoreFlowGapsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ProviderOption(Guid Id, string ProviderKey, string DisplayName, string PackageName);
    private record ProviderListBody(bool Success, List<ProviderOption> Data);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record TransactionData(Guid Id, string Status, long AmountCents);
    private record TransactionBody(bool Success, TransactionData Data);
    private record TransactionListBody(bool Success, List<TransactionData> Data);
    private record ProfileData(Guid Id, string Email, long? MonthlyIncomeCents);
    private record ProfileBody(bool Success, ProfileData Data);
    private record AdviceData(long MonthlyIncomeCents, long ProjectedBalanceCents, long? SuggestedDailyCutCents);
    private record ForecastData(long ProjectedSpendCents, AdviceData? Advice);
    private record ForecastBody(bool Success, ForecastData Data);
    private record CategoryData(Guid Id, string Slug);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record BudgetData(Guid Id);
    private record BudgetBody(bool Success, BudgetData Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Core Flow Test User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
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

    private async Task<Guid> CreateCashAccountAsync(string token)
    {
        var response = await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/financial-accounts", token,
            new CreateFinancialAccountRequest("Ví tiền mặt", AccountType.Cash, null, 50_000_000)));

        return (await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions))!.Data.Id;
    }

    private async Task<Guid> FoodCategoryIdAsync(string token)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/categories", token));
        var body = await response.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        return body!.Data.First(c => c.Slug == "food").Id;
    }

    private Task<HttpResponseMessage> SpendAsync(string token, Guid accountId, Guid categoryId, long amountCents)
        => _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token,
            new CreateManualTransactionRequest(
                accountId, categoryId, amountCents, TransactionType.Debit,
                DateTimeOffset.UtcNow, "Quán ăn", null)));

    // --------------------------------------------- Bước 1.2: danh sách app theo dõi

    [Fact]
    public async Task OnboardingCanListTheAppsToWatch()
    {
        // Không có endpoint này thì màn onboarding không dựng được: client buộc phải gửi
        // providerConfigId khi tạo ví mà không có cách nào biết id.
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/financial-accounts/providers", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var providers = (await response.Content.ReadFromJsonAsync<ProviderListBody>(JsonOptions))!.Data;

        providers.Select(p => p.ProviderKey).Should()
            .Contain(["mb_bank", "vietcombank", "momo", "zalopay"]);
        // packageName là thứ client Android dựng bộ lọc Tầng 1 từ đó.
        providers.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.PackageName));
    }

    [Fact]
    public async Task ProvidersWithAnUnverifiedPackageNameAreNotOffered()
    {
        // Techcombank/VPBank/ShopeePay seed ở trạng thái tắt: package_name chưa đối chiếu với
        // thông báo thật, và cho người dùng chọn là hứa một thứ sẽ không bao giờ chạy.
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/financial-accounts/providers", token));
        var providers = (await response.Content.ReadFromJsonAsync<ProviderListBody>(JsonOptions))!.Data;

        providers.Select(p => p.ProviderKey).Should()
            .NotContain(["techcombank", "vpbank", "shopeepay"]);
    }

    // --------------------------------------------- Bước 1.3: thu nhập hằng tháng

    [Fact]
    public async Task MonthlyIncomeRoundTripsThroughTheProfile()
    {
        var token = await RegisterAndLoginAsync();

        var initial = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/users/me", token));
        (await initial.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.MonthlyIncomeCents
            .Should().BeNull("người dùng mới chưa khai gì");

        var updated = await _client.SendAsync(Request(HttpMethod.Patch, "/api/v1/users/me", token,
            new UpdateProfileRequest("Core Flow Test User", 9_000_000)));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.MonthlyIncomeCents
            .Should().Be(9_000_000);

        // Gửi 0 để xoá — quay về "chưa khai", không phải "thu nhập bằng 0".
        var cleared = await _client.SendAsync(Request(HttpMethod.Patch, "/api/v1/users/me", token,
            new UpdateProfileRequest("Core Flow Test User", 0)));
        (await cleared.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.MonthlyIncomeCents
            .Should().BeNull();
    }

    [Fact]
    public async Task ForecastOnlyAdvisesOnceIncomeIsKnown()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);
        var categoryId = await FoodCategoryIdAsync(token);
        await SpendAsync(token, accountId, categoryId, 3_000_000);

        var before = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/reports/forecast", token));
        var beforeBody = (await before.Content.ReadFromJsonAsync<ForecastBody>(JsonOptions))!.Data;
        beforeBody.Advice.Should().BeNull();
        beforeBody.ProjectedSpendCents.Should().BeGreaterThan(0, "phần dự báo không phụ thuộc thu nhập");

        await _client.SendAsync(Request(HttpMethod.Patch, "/api/v1/users/me", token,
            new UpdateProfileRequest("Core Flow Test User", 1_000_000)));

        var after = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/reports/forecast", token));
        var advice = (await after.Content.ReadFromJsonAsync<ForecastBody>(JsonOptions))!.Data.Advice;

        advice.Should().NotBeNull();
        advice!.MonthlyIncomeCents.Should().Be(1_000_000);
        // Đã tiêu 3 triệu với thu nhập 1 triệu — chắc chắn bội chi.
        advice.ProjectedBalanceCents.Should().BeNegative();
        advice.SuggestedDailyCutCents.Should().BePositive();
    }

    [Fact]
    public async Task ANegativeIncomeIsRejected()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Request(HttpMethod.Patch, "/api/v1/users/me", token,
            new UpdateProfileRequest("Core Flow Test User", -1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --------------------------------------------- Bước 5.4: tab "Chờ duyệt"

    [Fact]
    public async Task PendingTabListsOnlyDraftsAndEmptiesOnceConfirmed()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);
        var categoryId = await FoodCategoryIdAsync(token);

        // Giao dịch tự nhập vào thẳng Confirmed — nó KHÔNG được nằm ở tab chờ duyệt.
        await SpendAsync(token, accountId, categoryId, 50_000);

        var drafts = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/transactions?status=Draft", token));
        drafts.StatusCode.Should().Be(HttpStatusCode.OK);
        (await drafts.Content.ReadFromJsonAsync<TransactionListBody>(JsonOptions))!.Data
            .Should().BeEmpty();

        var confirmed = await _client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/transactions?status=Confirmed", token));
        (await confirmed.Content.ReadFromJsonAsync<TransactionListBody>(JsonOptions))!.Data
            .Should().ContainSingle().Which.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task OmittingTheStatusFilterStillReturnsEverything()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);
        var categoryId = await FoodCategoryIdAsync(token);
        await SpendAsync(token, accountId, categoryId, 50_000);

        var all = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/transactions", token));

        (await all.Content.ReadFromJsonAsync<TransactionListBody>(JsonOptions))!.Data
            .Should().NotBeEmpty("client cũ không gửi status vẫn phải thấy lịch sử");
    }

    // --------------------------------------------- Flow 2 mục 2a: cảnh báo tức thì

    [Fact]
    public async Task CrossingABudgetThresholdIsDetectedOnTheTransactionItself()
    {
        // Không assert nội dung push (IPushNotificationService còn là stub log-only) mà assert
        // DẤU VẾT: cờ alert_*_sent_at được đóng ngay trong lượt tạo giao dịch. Trước Phase 10
        // chúng chỉ được đóng bởi job chạy mỗi giờ.
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);
        var categoryId = await FoodCategoryIdAsync(token);

        var budget = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/budgets", token,
            new CreateBudgetRequest(categoryId, 1_000_000)));
        budget.StatusCode.Should().Be(HttpStatusCode.Created);

        await SpendAsync(token, accountId, categoryId, 750_000);

        var summary = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/budgets", token));
        var raw = await summary.Content.ReadAsStringAsync();
        raw.Should().Contain("750000", "chi tiêu phải được ghi nhận ngay");

        // Chi thêm để vượt 100% — vẫn không lỗi, và không gửi lại mốc cũ.
        var overspend = await SpendAsync(token, accountId, categoryId, 400_000);
        overspend.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
