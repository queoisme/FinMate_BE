using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class ReportsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ReportsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record CategoryData(Guid Id, string Slug, bool IsSystem);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record MonthlySummaryData(
        DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, long TotalSpentCents, long TotalIncomeCents,
        long NetCents, int TransactionCount, long PrevMonthSpentCents, double? ChangePercent);
    private record MonthlySummaryBody(bool Success, MonthlySummaryData Data);
    private record BreakdownItem(Guid? CategoryId, string CategoryName, string? CategorySlug, long SpentCents, int Percent);
    private record BreakdownData(DateTimeOffset PeriodStart, long TotalSpentCents, List<BreakdownItem> Items);
    private record BreakdownBody(bool Success, BreakdownData Data);
    private record TimelineTransaction(Guid Id, long AmountCents);
    private record TimelineDay(DateOnly Date, long TotalSpentCents, long TotalIncomeCents, List<TimelineTransaction> Transactions);
    private record TimelineBody(bool Success, List<TimelineDay> Data);
    private record ForecastData(
        long SpentSoFarCents, long ProjectedSpendCents, int DaysElapsed, int DaysInMonth,
        int BasedOnDays, string Confidence);
    private record ForecastBody(bool Success, ForecastData Data);
    private record InsightData(Guid Id, string InsightType, string Title, bool IsRead);
    private record InsightListBody(bool Success, List<InsightData> Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Reports Test User"));

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> PostAsync(string url, string token, object body)
    {
        var request = Authed(HttpMethod.Post, url, token);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> SystemCategoryAsync(string token, string slug)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/categories", token));
        var body = await response.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        return body!.Data.First(c => c.IsSystem && c.Slug == slug).Id;
    }

    private async Task<Guid> CashAccountAsync(string token)
    {
        var response = await PostAsync("/api/v1/financial-accounts", token, new
        {
            accountType = "Cash",
            accountName = "Ví tiền mặt",
            initialBalanceCents = 50_000_000L,
        });
        return (await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions))!.Data.Id;
    }

    private Task SpendAsync(string token, Guid accountId, Guid? categoryId, long amount, DateTimeOffset at, string type = "Debit")
        => PostAsync("/api/v1/transactions", token, new
        {
            financialAccountId = accountId,
            categoryId,
            amountCents = amount,
            transactionType = type,
            transactedAt = at,
            merchantName = "Test",
        });

    [Fact]
    public async Task MonthlySummary_CountsThisMonthsSpendAndIncome()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");

        await SpendAsync(token, accountId, food, 300_000, DateTimeOffset.UtcNow);
        await SpendAsync(token, accountId, food, 200_000, DateTimeOffset.UtcNow);
        await SpendAsync(token, accountId, null, 10_000_000, DateTimeOffset.UtcNow, "Credit");

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/monthly-summary", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<MonthlySummaryBody>(JsonOptions))!.Data;

        data.TotalSpentCents.Should().Be(500_000);
        data.TotalIncomeCents.Should().Be(10_000_000);
        data.NetCents.Should().Be(9_500_000);
        data.TransactionCount.Should().Be(3);
        // Mốc chu kỳ theo giờ VN, không phải biểu diễn UTC.
        data.PeriodStart.Offset.Should().Be(TimeSpan.FromHours(7));
        data.PeriodStart.Day.Should().Be(1);
    }

    [Fact]
    public async Task MonthlySummary_NoPreviousMonthData_LeavesChangeNull()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        await SpendAsync(token, accountId, null, 100_000, DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/monthly-summary", token));
        var data = (await response.Content.ReadFromJsonAsync<MonthlySummaryBody>(JsonOptions))!.Data;

        data.PrevMonthSpentCents.Should().Be(0);
        data.ChangePercent.Should().BeNull();
    }

    [Fact]
    public async Task CategoryBreakdown_SplitsSpendAndSumsToOneHundredPercent()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");
        var transport = await SystemCategoryAsync(token, "transport");

        await SpendAsync(token, accountId, food, 600_000, DateTimeOffset.UtcNow);
        await SpendAsync(token, accountId, transport, 400_000, DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/category-breakdown", token));
        var data = (await response.Content.ReadFromJsonAsync<BreakdownBody>(JsonOptions))!.Data;

        data.TotalSpentCents.Should().Be(1_000_000);
        data.Items.Sum(i => i.Percent).Should().Be(100);
        data.Items.Single(i => i.CategorySlug == "food").Percent.Should().Be(60);
    }

    [Fact]
    public async Task CategoryBreakdown_UncategorizedSpend_AppearsAsItsOwnRow()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");

        await SpendAsync(token, accountId, food, 700_000, DateTimeOffset.UtcNow);
        await SpendAsync(token, accountId, null, 300_000, DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/category-breakdown", token));
        var data = (await response.Content.ReadFromJsonAsync<BreakdownBody>(JsonOptions))!.Data;

        data.Items.Single(i => i.CategoryId is null).SpentCents.Should().Be(300_000);
    }

    [Fact]
    public async Task CategoryBreakdown_IncomeIsNotPartOfSpending()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        var income = await SystemCategoryAsync(token, "income");

        await SpendAsync(token, accountId, income, 20_000_000, DateTimeOffset.UtcNow, "Credit");

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/category-breakdown", token));
        var data = (await response.Content.ReadFromJsonAsync<BreakdownBody>(JsonOptions))!.Data;

        data.TotalSpentCents.Should().Be(0);
        data.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeline_GroupsTransactionsByDayWithDailyTotals()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        var now = DateTimeOffset.UtcNow;

        await SpendAsync(token, accountId, null, 100_000, now);
        await SpendAsync(token, accountId, null, 50_000, now.AddMinutes(-5));
        await SpendAsync(token, accountId, null, 70_000, now.AddDays(-3));

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/timeline?limit=50", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var days = (await response.Content.ReadFromJsonAsync<TimelineBody>(JsonOptions))!.Data;

        days.Should().HaveCount(2);
        days[0].Transactions.Should().HaveCount(2);
        days[0].TotalSpentCents.Should().Be(150_000);
        days[1].TotalSpentCents.Should().Be(70_000);
    }

    [Fact]
    public async Task Forecast_KeepsActualSpendAndReportsConfidence()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        await SpendAsync(token, accountId, null, 250_000, DateTimeOffset.UtcNow);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/forecast", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<ForecastBody>(JsonOptions))!.Data;

        data.SpentSoFarCents.Should().Be(250_000);
        // Dự báo không bao giờ được thấp hơn phần đã tiêu thật.
        data.ProjectedSpendCents.Should().BeGreaterThanOrEqualTo(250_000);
        data.Confidence.Should().BeOneOf("low", "medium", "high");
        data.DaysInMonth.Should().BeInRange(28, 31);
    }

    [Fact]
    public async Task Forecast_NewUserWithNoTransactions_ReturnsZeroRatherThanFailing()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/forecast", token));
        var data = (await response.Content.ReadFromJsonAsync<ForecastBody>(JsonOptions))!.Data;

        data.SpentSoFarCents.Should().Be(0);
        data.ProjectedSpendCents.Should().Be(0);
        data.Confidence.Should().Be("low");
    }

    [Fact]
    public async Task Insights_NewUser_HasNoneYet()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/insights", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<InsightListBody>(JsonOptions))!.Data;

        data.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkInsightRead_UnknownId_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            Authed(HttpMethod.Patch, $"/api/v1/reports/insights/{Guid.NewGuid()}/read", token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reports_OnlyEverSeeTheCallersOwnTransactions()
    {
        var other = await RegisterAndLoginAsync();
        var otherAccount = await CashAccountAsync(other);
        await SpendAsync(other, otherAccount, null, 9_000_000, DateTimeOffset.UtcNow);

        var token = await RegisterAndLoginAsync();
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/reports/monthly-summary", token));
        var data = (await response.Content.ReadFromJsonAsync<MonthlySummaryBody>(JsonOptions))!.Data;

        data.TotalSpentCents.Should().Be(0);
    }
}
