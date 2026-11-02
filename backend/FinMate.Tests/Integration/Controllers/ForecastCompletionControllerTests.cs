using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Dự báo tài chính đợt 3 qua API thật: khoản chi bất thường được đánh dấu, báo đúng một lần và nhận phản hồi; dự báo theo danh mục thành ngân sách kỳ tới;
/// ngày đạt mục tiêu tiết kiệm. Mọi thứ tách theo người dùng.
/// </summary>
[Collection("Integration")]
public class ForecastCompletionControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastCompletionControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug);
    private record AlertData(Guid Id, string Kind);
    private record AnomalyData(Guid Id, Guid TransactionId, string Kind, string Severity, long AmountCents, long TypicalCents, string? MerchantName, string? Resolution);
    private record FeedbackData(AnomalyData Anomaly, string? Guidance);
    private record ForecastItemData(Guid? CategoryId, long ProjectedCents, string Basis, string Confidence, long? CurrentLimitCents, long? SuggestedLimitCents);
    private record CategoryForecastData(string Month, List<string> AvailableMonths, bool EnoughHistory, bool SeasonalAdjusted, List<ForecastItemData> Items);
    private record AppliedData(Guid BudgetId, Guid CategoryId, long LimitCents, string Effective);
    private record SummaryItemData(Guid? CategoryId, long LimitCents);
    private record SummaryData(List<SummaryItemData> CategoryBudgets);
    private record GoalForecastData(string ForecastStatus, long AverageMonthlyCents, string Confidence, bool IsScenario, long? RequiredMonthlyCents, DateOnly? LikelyDate);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Forecast Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendRawAsync(method, url, token, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private async Task<Guid> CategoryAsync(string token, string slug)
        => (await SendAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token)).First(c => c.Slug == slug).Id;

    private Task<IdData> SpendAsync(string token, Guid wallet, Guid category, long amount, string merchant, DateTimeOffset at)
        => SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = wallet, categoryId = category, amountCents = amount, transactionType = "Debit", transactedAt = at, merchantName = merchant,
        }, HttpStatusCode.Created);

    private async Task CheckAsync(string token)
    {
        var userId = (await SendAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token)).Id;
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IForecastAlertService>().CheckUserAsync(userId);
    }

    [Fact]
    public async Task UnusualSpend_IsFlaggedAndAlertedOnce_AndFeedbackIsRecorded()
    {
        var token = await NewUserTokenAsync();
        var wallet = (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = 50_000_000 }, HttpStatusCode.Created)).Id;
        var food = await CategoryAsync(token, "food");
        var now = DateTimeOffset.UtcNow;
        for (var day = 1; day <= 6; day++)
        {
            await SpendAsync(token, wallet, food, 100_000, $"Quán {day}", now.AddDays(-day));
        }

        var big = await SpendAsync(token, wallet, food, 700_000, "Nhà hàng mới", now.AddMinutes(-1));
        await CheckAsync(token);
        await CheckAsync(token);

        var open = await SendAsync<List<AnomalyData>>(HttpMethod.Get, "/api/v1/forecast/anomalies", token);
        var anomaly = open.Should().ContainSingle().Subject;
        anomaly.TransactionId.Should().Be(big.Id);
        anomaly.Kind.Should().Be("category_spike");
        anomaly.Severity.Should().Be("high");
        anomaly.TypicalCents.Should().Be(100_000);
        anomaly.MerchantName.Should().Be("Nhà hàng mới");
        (await SendAsync<List<AlertData>>(HttpMethod.Get, "/api/v1/forecast/alerts", token)).Should().ContainSingle(a => a.Kind == "anomaly");

        var oneOff = await SendAsync<FeedbackData>(HttpMethod.Post, $"/api/v1/forecast/anomalies/{anomaly.Id}/feedback", token, new { resolution = "one_off" });
        oneOff.Anomaly.Resolution.Should().Be("one_off");
        oneOff.Guidance.Should().BeNull();
        (await SendAsync<List<AnomalyData>>(HttpMethod.Get, "/api/v1/forecast/anomalies", token)).Should().BeEmpty();
        (await SendAsync<List<AnomalyData>>(HttpMethod.Get, "/api/v1/forecast/anomalies?status=all", token)).Should().ContainSingle();

        var suspicious = await SendAsync<FeedbackData>(HttpMethod.Post, $"/api/v1/forecast/anomalies/{anomaly.Id}/feedback", token, new { resolution = "suspicious" });
        suspicious.Guidance.Should().NotBeNullOrWhiteSpace();

        (await SendRawAsync(HttpMethod.Post, $"/api/v1/forecast/anomalies/{anomaly.Id}/feedback", token, new { resolution = "maybe" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stranger = await NewUserTokenAsync();
        (await SendRawAsync(HttpMethod.Post, $"/api/v1/forecast/anomalies/{anomaly.Id}/feedback", stranger, new { resolution = "normal" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync<List<AnomalyData>>(HttpMethod.Get, "/api/v1/forecast/anomalies?status=all", stranger)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneOffFeedback_TakesTheTransactionOutOfForecastData()
    {
        var token = await NewUserTokenAsync();
        var wallet = (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = 50_000_000 }, HttpStatusCode.Created)).Id;
        var food = await CategoryAsync(token, "food");
        var now = DateTimeOffset.UtcNow;
        for (var day = 1; day <= 5; day++)
        {
            await SpendAsync(token, wallet, food, 100_000, $"Quán {day}", now.AddDays(-day));
        }

        var big = await SpendAsync(token, wallet, food, 400_000, "Tiệc", now.AddMinutes(-1));
        await CheckAsync(token);
        var anomaly = (await SendAsync<List<AnomalyData>>(HttpMethod.Get, "/api/v1/forecast/anomalies", token)).Single();
        anomaly.Severity.Should().Be("normal");
        (await SendAsync<List<AlertData>>(HttpMethod.Get, "/api/v1/forecast/alerts", token)).Should().NotContain(a => a.Kind == "anomaly");

        await SendAsync<FeedbackData>(HttpMethod.Post, $"/api/v1/forecast/anomalies/{anomaly.Id}/feedback", token, new { resolution = "one_off" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var transaction = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == big.Id);
        transaction.ExcludeFromForecast.Should().BeTrue();
        transaction.ForecastExclusionReason.Should().Be(ForecastExclusionReason.OneOff);
    }

    [Fact]
    public async Task CategoryForecast_BecomesNextMonthsBudget_WithoutTouchingThisMonth()
    {
        var token = await NewUserTokenAsync();
        var food = await CategoryAsync(token, "food");
        var fun = await CategoryAsync(token, "entertainment");
        await SendAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", token, new { categoryId = food, limitCents = 2_000_000 }, HttpStatusCode.Created);

        var forecast = await SendAsync<CategoryForecastData>(HttpMethod.Get, "/api/v1/forecast/categories", token);
        var nextMonth = VietnamTime.Today().AddMonths(1);
        forecast.Month.Should().Be($"{nextMonth.Year:D4}-{nextMonth.Month:D2}");
        forecast.AvailableMonths.Should().HaveCount(2);
        forecast.EnoughHistory.Should().BeFalse();
        forecast.SeasonalAdjusted.Should().BeFalse();
        var foodItem = forecast.Items.Should().ContainSingle(i => i.CategoryId == food).Subject;
        foodItem.Basis.Should().Be("budget");
        foodItem.Confidence.Should().Be("low");
        foodItem.SuggestedLimitCents.Should().Be(2_000_000);

        (await SendRawAsync(HttpMethod.Get, "/api/v1/forecast/categories?month=2020-01", token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var applied = await SendAsync<List<AppliedData>>(HttpMethod.Post, "/api/v1/forecast/categories/apply", token, new
        {
            items = new[] { new { categoryId = food, limitCents = 2_500_000L }, new { categoryId = fun, limitCents = 1_000_000L } },
        });
        applied.Single(a => a.CategoryId == food).Effective.Should().Be("next_period");
        applied.Single(a => a.CategoryId == fun).Effective.Should().Be("now");

        var today = VietnamTime.Today();
        var thisMonth = await SendAsync<SummaryData>(HttpMethod.Get, $"/api/v1/budgets?year={today.Year}&month={today.Month}", token);
        thisMonth.CategoryBudgets.Single(b => b.CategoryId == food).LimitCents.Should().Be(2_000_000);
        thisMonth.CategoryBudgets.Single(b => b.CategoryId == fun).LimitCents.Should().Be(1_000_000);
        var next = await SendAsync<SummaryData>(HttpMethod.Get, $"/api/v1/budgets?year={nextMonth.Year}&month={nextMonth.Month}", token);
        next.CategoryBudgets.Single(b => b.CategoryId == food).LimitCents.Should().Be(2_500_000);

        (await SendRawAsync(HttpMethod.Post, "/api/v1/forecast/categories/apply", token, new { items = new[] { new { categoryId = food, limitCents = 0L } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GoalForecast_UsesContributions_AndScenarioCanFixADeadline()
    {
        var token = await NewUserTokenAsync();
        var goal = await SendAsync<IdData>(HttpMethod.Post, "/api/v1/saving-goals", token,
            new { name = "Laptop", targetCents = 10_000_000, deadline = DateTimeOffset.UtcNow.AddDays(180) }, HttpStatusCode.Created);
        var contributed = await SendRawAsync(HttpMethod.Post, $"/api/v1/saving-goals/{goal.Id}/contribute", token, new { amountCents = 1_000_000 });
        contributed.IsSuccessStatusCode.Should().BeTrue(await contributed.Content.ReadAsStringAsync());

        var forecast = await SendAsync<GoalForecastData>(HttpMethod.Get, $"/api/v1/forecast/goals/{goal.Id}", token);
        forecast.ForecastStatus.Should().Be("behind");
        forecast.AverageMonthlyCents.Should().Be(1_000_000);
        forecast.Confidence.Should().Be("low");
        forecast.RequiredMonthlyCents.Should().BeGreaterThan(1_000_000);

        var scenario = await SendAsync<GoalForecastData>(HttpMethod.Get, $"/api/v1/forecast/goals/{goal.Id}?monthlyContributionCents=2000000", token);
        scenario.IsScenario.Should().BeTrue();
        scenario.ForecastStatus.Should().Be("on_track");

        (await SendRawAsync(HttpMethod.Get, $"/api/v1/forecast/goals/{goal.Id}?monthlyContributionCents=-5", token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stranger = await NewUserTokenAsync();
        (await SendRawAsync(HttpMethod.Get, $"/api/v1/forecast/goals/{goal.Id}", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
