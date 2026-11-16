using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Chi tiết giao dịch mở rộng, gắn mục tiêu, chia sẻ biên lai, khoản định kỳ từ giao dịch và gợi ý chat (yêu cầu của đội FE).</summary>
[Collection("Integration")]
public class TransactionExtrasControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public TransactionExtrasControllerTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> LoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Extras Test"));
        var r = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> SendAsync(string token, HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: Json);
        }

        var res = await _client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<(Guid account, Guid tx)> SeedTransactionAsync(string token, string merchant = "Netflix")
    {
        var acc = await SendAsync(token, HttpMethod.Post, "/api/v1/financial-accounts",
            new CreateFinancialAccountRequest("Ví test", AccountType.Cash, null, 10_000_000), HttpStatusCode.Created);
        var accountId = acc.GetProperty("data").GetProperty("id").GetGuid();
        var tx = await SendAsync(token, HttpMethod.Post, "/api/v1/transactions",
            new CreateManualTransactionRequest(accountId, null, 260_000, TransactionType.Debit, DateTimeOffset.UtcNow.AddDays(-2), merchant, "Gói tháng"),
            HttpStatusCode.Created);
        return (accountId, tx.GetProperty("data").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Detail_has_source_detail_and_no_goal_by_default()
    {
        var token = await LoginAsync();
        var (_, tx) = await SeedTransactionAsync(token);

        var data = (await SendAsync(token, HttpMethod.Get, $"/api/v1/transactions/{tx}", null, HttpStatusCode.OK)).GetProperty("data");

        data.GetProperty("sourceDetail").GetProperty("channel").GetString().Should().Be("Manual");
        data.GetProperty("goalId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("aiClassification").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Goal_can_be_attached_and_detached_and_other_users_goal_is_404()
    {
        var token = await LoginAsync();
        var (_, tx) = await SeedTransactionAsync(token);
        var goal = (await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals",
            new CreateSavingGoalRequest("Mua laptop", 20_000_000, null), HttpStatusCode.Created)).GetProperty("data").GetProperty("id").GetGuid();

        var attached = (await SendAsync(token, HttpMethod.Put, $"/api/v1/transactions/{tx}/goal", new AttachGoalRequest(goal), HttpStatusCode.OK)).GetProperty("data");
        attached.GetProperty("goalId").GetGuid().Should().Be(goal);
        attached.GetProperty("goalName").GetString().Should().Be("Mua laptop");

        var detail = (await SendAsync(token, HttpMethod.Get, $"/api/v1/transactions/{tx}", null, HttpStatusCode.OK)).GetProperty("data");
        detail.GetProperty("goalName").GetString().Should().Be("Mua laptop");

        var detached = (await SendAsync(token, HttpMethod.Delete, $"/api/v1/transactions/{tx}/goal", null, HttpStatusCode.OK)).GetProperty("data");
        detached.GetProperty("goalId").ValueKind.Should().Be(JsonValueKind.Null);

        var other = await LoginAsync();
        await SendAsync(other, HttpMethod.Put, $"/api/v1/transactions/{tx}/goal", new AttachGoalRequest(goal), HttpStatusCode.NotFound);
        await SendAsync(token, HttpMethod.Put, $"/api/v1/transactions/{tx}/goal", new AttachGoalRequest(Guid.NewGuid()), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShareReceipt_returns_text_with_amount_and_merchant()
    {
        var token = await LoginAsync();
        var (_, tx) = await SeedTransactionAsync(token);

        var data = (await SendAsync(token, HttpMethod.Post, $"/api/v1/transactions/{tx}/share-receipt", null, HttpStatusCode.OK)).GetProperty("data");

        data.GetProperty("text").GetString().Should().Contain("260.000").And.Contain("Netflix");
    }

    [Fact]
    public async Task Recurring_from_transaction_creates_once_then_conflicts()
    {
        var token = await LoginAsync();
        var (_, tx) = await SeedTransactionAsync(token);

        var created = (await SendAsync(token, HttpMethod.Post, $"/api/v1/recurring-items/from-transaction/{tx}", null, HttpStatusCode.Created)).GetProperty("data");
        created.GetProperty("name").GetString().Should().Be("Netflix");
        created.GetProperty("amountCents").GetInt64().Should().Be(260_000);
        created.GetProperty("cadence").GetString().Should().Be("monthly");
        created.GetProperty("direction").GetString().Should().Be("outflow");

        var conflict = await SendAsync(token, HttpMethod.Post, $"/api/v1/recurring-items/from-transaction/{tx}", null, HttpStatusCode.Conflict);
        conflict.GetProperty("error").GetProperty("code").GetString().Should().Be("RECURRING_ITEM_DUPLICATE");
    }

    [Fact]
    public async Task PeriodSummary_sums_any_date_range_and_rejects_bad_ranges()
    {
        var token = await LoginAsync();
        var (_, _) = await SeedTransactionAsync(token);   // chi 260.000 vào 2 ngày trước

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wide = (await SendAsync(token, HttpMethod.Get,
            $"/api/v1/reports/period-summary?fromDate={today.AddYears(-2):yyyy-MM-dd}&toDate={today.AddDays(1):yyyy-MM-dd}", null, HttpStatusCode.OK)).GetProperty("data");
        wide.GetProperty("totalSpentCents").GetInt64().Should().Be(260_000);
        wide.GetProperty("netCents").GetInt64().Should().Be(-260_000);
        wide.GetProperty("transactionCount").GetInt32().Should().Be(1);

        var lastYear = (await SendAsync(token, HttpMethod.Get,
            $"/api/v1/reports/period-summary?fromDate={today.AddYears(-1):yyyy-MM-dd}&toDate={today.AddYears(-1).AddDays(30):yyyy-MM-dd}", null, HttpStatusCode.OK)).GetProperty("data");
        lastYear.GetProperty("totalSpentCents").GetInt64().Should().Be(0);

        var reversed = await SendAsync(token, HttpMethod.Get,
            $"/api/v1/reports/period-summary?fromDate={today:yyyy-MM-dd}&toDate={today.AddDays(-1):yyyy-MM-dd}", null, HttpStatusCode.UnprocessableEntity);
        reversed.GetProperty("error").GetProperty("code").GetString().Should().Be("REPORT_INVALID_RANGE");

        var tooLong = await SendAsync(token, HttpMethod.Get,
            $"/api/v1/reports/period-summary?fromDate={today.AddYears(-5):yyyy-MM-dd}&toDate={today:yyyy-MM-dd}", null, HttpStatusCode.UnprocessableEntity);
        tooLong.GetProperty("error").GetProperty("code").GetString().Should().Be("REPORT_RANGE_TOO_LONG");
    }

    [Fact]
    public async Task Suggestions_follow_context_and_respect_the_cap()
    {
        var token = await LoginAsync();

        var first = (await SendAsync(token, HttpMethod.Get, "/api/v1/assistant/suggestions", null, HttpStatusCode.OK)).GetProperty("data");
        var keys = first.EnumerateArray().Select(e => e.GetProperty("key").GetString()).ToList();
        keys.Should().Contain("add_coffee");          // chưa có giao dịch hôm nay
        keys.Should().NotContain("budget_left");      // chưa có ngân sách
        keys.Should().NotContain("goal_progress");    // chưa có mục tiêu
        keys.Count.Should().BeLessThanOrEqualTo(4);

        await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals", new CreateSavingGoalRequest("Du lịch", 5_000_000, null), HttpStatusCode.Created);
        var after = (await SendAsync(token, HttpMethod.Get, "/api/v1/assistant/suggestions", null, HttpStatusCode.OK)).GetProperty("data");
        after.EnumerateArray().Select(e => e.GetProperty("key").GetString()).Should().Contain("goal_progress");
    }
}
