using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>G3 (mục tiêu: biểu tượng, số đã có, nhịp góp, nháp, khả thi mở rộng) và F1/F2 (báo cáo: kỳ trước, số giao dịch, ngân sách theo danh mục).</summary>
[Collection("Integration")]
public class GoalsAndReportsExtrasControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public GoalsAndReportsExtrasControllerTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> LoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Goals Test"));
        var r = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> SendAsync(string token, HttpMethod method, string url, object? body, params HttpStatusCode[] expected)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: Json);
        }

        var res = await _client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        expected.Should().Contain(res.StatusCode, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static DateTimeOffset Future(int days) => DateTimeOffset.UtcNow.AddDays(days);

    [Fact]
    public async Task Goal_keeps_icon_initial_saved_and_frequency_and_patch_can_change_them()
    {
        var token = await LoginAsync();

        var created = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals",
            new CreateSavingGoalRequest("Du lịch", 10_000_000, Future(120), null, "plane", 2_000_000, GoalContributionFrequency.Weekly),
            HttpStatusCode.Created)).GetProperty("data");

        created.GetProperty("icon").GetString().Should().Be("plane");
        created.GetProperty("initialSavedCents").GetInt64().Should().Be(2_000_000);
        created.GetProperty("savedCents").GetInt64().Should().Be(2_000_000);
        created.GetProperty("remainingCents").GetInt64().Should().Be(8_000_000);
        created.GetProperty("contributionFrequency").GetString().Should().Be("weekly");
        created.GetProperty("status").GetString().Should().Be("active");

        var id = created.GetProperty("id").GetGuid();
        var patched = (await SendAsync(token, HttpMethod.Patch, $"/api/v1/saving-goals/{id}",
            new UpdateSavingGoalRequest("Du lịch Nhật", 10_000_000, Future(120), "mountain", GoalContributionFrequency.Monthly),
            HttpStatusCode.OK)).GetProperty("data");
        patched.GetProperty("icon").GetString().Should().Be("mountain");
        patched.GetProperty("contributionFrequency").GetString().Should().Be("monthly");

        var cleared = (await SendAsync(token, HttpMethod.Patch, $"/api/v1/saving-goals/{id}",
            new UpdateSavingGoalRequest("Du lịch Nhật", 10_000_000, Future(120), ""), HttpStatusCode.OK)).GetProperty("data");
        cleared.GetProperty("icon").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("contributionFrequency").GetString().Should().Be("monthly");   // null = giữ nguyên
    }

    [Fact]
    public async Task Initial_saved_must_be_below_target()
    {
        var token = await LoginAsync();

        await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals",
            new CreateSavingGoalRequest("Quá đủ", 1_000_000, null, null, null, 1_000_000),
            HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Draft_goal_cannot_receive_money_until_activated()
    {
        var token = await LoginAsync();
        var draft = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals",
            new CreateSavingGoalRequest("Xe máy", 30_000_000, Future(200), null, null, null, null, true), HttpStatusCode.Created)).GetProperty("data");
        draft.GetProperty("status").GetString().Should().Be("draft");
        var id = draft.GetProperty("id").GetGuid();

        var blocked = await SendAsync(token, HttpMethod.Post, $"/api/v1/saving-goals/{id}/contribute",
            new ContributeToGoalRequest(100_000, null, null), HttpStatusCode.UnprocessableEntity);
        blocked.GetProperty("error").GetProperty("code").GetString().Should().Be("SAVING_GOAL_NOT_ACTIVE");

        var drafts = (await SendAsync(token, HttpMethod.Get, "/api/v1/saving-goals?status=draft", null, HttpStatusCode.OK)).GetProperty("data");
        drafts.GetArrayLength().Should().Be(1);

        var active = (await SendAsync(token, HttpMethod.Post, $"/api/v1/saving-goals/{id}/activate", null, HttpStatusCode.OK)).GetProperty("data");
        active.GetProperty("status").GetString().Should().Be("active");

        var again = await SendAsync(token, HttpMethod.Post, $"/api/v1/saving-goals/{id}/activate", null, HttpStatusCode.UnprocessableEntity);
        again.GetProperty("error").GetProperty("code").GetString().Should().Be("SAVING_GOAL_NOT_DRAFT");

        await SendAsync(token, HttpMethod.Post, $"/api/v1/saving-goals/{id}/contribute",
            new ContributeToGoalRequest(100_000, null, null), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Feasibility_returns_week_day_percent_income_and_auto_save()
    {
        var token = await LoginAsync();

        var noIncome = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals/feasibility",
            new FeasibilityRequest(12_000_000, 0, Future(180)), HttpStatusCode.OK)).GetProperty("data");
        noIncome.GetProperty("requiredPerMonthCents").GetInt64().Should().BeGreaterThan(0);
        noIncome.GetProperty("requiredPerWeekCents").GetInt64().Should().BeGreaterThan(0);
        noIncome.GetProperty("requiredPerDayCents").GetInt64().Should().BeGreaterThan(0);
        noIncome.GetProperty("feasibilityPercent").ValueKind.Should().Be(JsonValueKind.Null);
        noIncome.GetProperty("autoSaveSuggestion").ValueKind.Should().Be(JsonValueKind.Null);

        await SendAsync(token, HttpMethod.Patch, "/api/v1/users/me", new UpdateProfileRequest("Goals Test", 20_000_000), HttpStatusCode.OK);

        var easy = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals/feasibility",
            new FeasibilityRequest(12_000_000, 0, Future(180)), HttpStatusCode.OK)).GetProperty("data");
        easy.GetProperty("monthlyIncomeCents").GetInt64().Should().Be(20_000_000);
        easy.GetProperty("feasibilityPercent").GetInt32().Should().Be(100);
        var auto = easy.GetProperty("autoSaveSuggestion");
        auto.GetProperty("frequency").GetString().Should().Be("monthly");
        auto.GetProperty("amountCents").GetInt64().Should().Be(easy.GetProperty("requiredPerMonthCents").GetInt64());
        auto.GetProperty("coversTarget").GetBoolean().Should().BeTrue();

        var weekly = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals/feasibility",
            new FeasibilityRequest(12_000_000, 0, Future(180), GoalContributionFrequency.Weekly), HttpStatusCode.OK)).GetProperty("data");
        weekly.GetProperty("autoSaveSuggestion").GetProperty("frequency").GetString().Should().Be("weekly");

        var hard = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals/feasibility",
            new FeasibilityRequest(600_000_000, 0, Future(30)), HttpStatusCode.OK)).GetProperty("data");
        hard.GetProperty("isFeasible").GetBoolean().Should().BeFalse();
        hard.GetProperty("feasibilityPercent").GetInt32().Should().BeInRange(0, 99);
        hard.GetProperty("autoSaveSuggestion").GetProperty("coversTarget").GetBoolean().Should().BeFalse();
    }

    private async Task<(string token, Guid account, Guid food)> SeedSpendingAsync()
    {
        var token = await LoginAsync();
        var acc = (await SendAsync(token, HttpMethod.Post, "/api/v1/financial-accounts",
            new CreateFinancialAccountRequest("Ví", AccountType.Cash, null, 100_000_000), HttpStatusCode.Created)).GetProperty("data").GetProperty("id").GetGuid();
        var cats = (await SendAsync(token, HttpMethod.Get, "/api/v1/categories", null, HttpStatusCode.OK)).GetProperty("data");
        var food = cats.EnumerateArray().First(c => c.GetProperty("slug").GetString() == "food").GetProperty("id").GetGuid();
        return (token, acc, food);
    }

    private Task Tx(string token, Guid acc, Guid? cat, long cents, DateTimeOffset at, TransactionType type = TransactionType.Debit)
        => SendAsync(token, HttpMethod.Post, "/api/v1/transactions",
            new CreateManualTransactionRequest(acc, cat, cents, type, at, null, "x"), HttpStatusCode.Created);

    [Fact]
    public async Task Monthly_summary_has_previous_month_income_and_change()
    {
        var (token, acc, _) = await SeedSpendingAsync();
        var now = DateTimeOffset.UtcNow;
        var inThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 12, 0, 0, TimeSpan.Zero).AddHours(5);
        await Tx(token, acc, null, 3_000_000, inThisMonth.AddMonths(-1), TransactionType.Credit);
        await Tx(token, acc, null, 4_500_000, inThisMonth, TransactionType.Credit);

        var data = (await SendAsync(token, HttpMethod.Get, "/api/v1/reports/monthly-summary", null, HttpStatusCode.OK)).GetProperty("data");

        data.GetProperty("prevMonthIncomeCents").GetInt64().Should().Be(3_000_000);
        data.GetProperty("incomeChangePercent").GetDouble().Should().Be(50.0);
    }

    [Fact]
    public async Task Period_summary_compares_with_the_previous_period_of_equal_length()
    {
        var (token, acc, _) = await SeedSpendingAsync();
        var today = DateTimeOffset.UtcNow;
        await Tx(token, acc, null, 100_000, today.AddDays(-1));
        await Tx(token, acc, null, 200_000, today.AddDays(-8));   // nằm trong kỳ 7 ngày liền trước

        var to = DateOnly.FromDateTime(today.UtcDateTime);
        var from = to.AddDays(-6);
        var data = (await SendAsync(token, HttpMethod.Get,
            $"/api/v1/reports/period-summary?fromDate={from:yyyy-MM-dd}&toDate={to:yyyy-MM-dd}", null, HttpStatusCode.OK)).GetProperty("data");

        data.GetProperty("totalSpentCents").GetInt64().Should().Be(100_000);
        data.GetProperty("prevFrom").GetString().Should().Be(from.AddDays(-7).ToString("yyyy-MM-dd"));
        data.GetProperty("prevTo").GetString().Should().Be(from.AddDays(-1).ToString("yyyy-MM-dd"));
        data.GetProperty("prevTotalSpentCents").GetInt64().Should().Be(200_000);
        data.GetProperty("spentChangePercent").GetDouble().Should().Be(-50.0);
        data.GetProperty("incomeChangePercent").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Category_breakdown_has_prev_saved_count_and_budget()
    {
        var (token, acc, food) = await SeedSpendingAsync();
        var now = DateTimeOffset.UtcNow;
        var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 12, 0, 0, TimeSpan.Zero).AddHours(5);

        await SendAsync(token, HttpMethod.Post, "/api/v1/budgets", new CreateBudgetRequest(food, 1_000_000), HttpStatusCode.Created);
        await Tx(token, acc, food, 500_000, thisMonth.AddMonths(-1));
        await Tx(token, acc, food, 200_000, thisMonth);
        await Tx(token, acc, food, 100_000, thisMonth.AddHours(1));

        var items = (await SendAsync(token, HttpMethod.Get, "/api/v1/reports/category-breakdown", null, HttpStatusCode.OK)).GetProperty("data").GetProperty("items");
        var item = items.EnumerateArray().Single(i => i.GetProperty("categoryId").GetGuid() == food);

        item.GetProperty("spentCents").GetInt64().Should().Be(300_000);
        item.GetProperty("prevSpentCents").GetInt64().Should().Be(500_000);
        item.GetProperty("savedCents").GetInt64().Should().Be(200_000);
        item.GetProperty("transactionCount").GetInt32().Should().Be(2);
        item.GetProperty("budgetLimitCents").GetInt64().Should().Be(1_000_000);
        item.GetProperty("budgetSpentCents").GetInt64().Should().Be(300_000);

        // Tab thu không mang số liệu ngân sách hay "tiết kiệm".
        await Tx(token, acc, food, 700_000, thisMonth, TransactionType.Credit);
        var income = (await SendAsync(token, HttpMethod.Get, "/api/v1/reports/category-breakdown?type=income", null, HttpStatusCode.OK)).GetProperty("data");
        income.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("budgetLimitCents").ValueKind == JsonValueKind.Null);
    }
}
