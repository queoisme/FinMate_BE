using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Dự báo tài chính đợt 2 qua API thật: cài đặt cảnh báo, cảnh báo số dư thấp đi vào hộp thư đúng một lần, phản hồi "không chính xác",
/// chuyển hạn mức ngân sách, dự báo theo ngân sách và lịch thu nhập — mọi thứ tách theo người dùng.
/// </summary>
[Collection("Integration")]
public class ForecastAlertsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastAlertsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug);
    private record SettingsData(long? LowBalanceThresholdCents, string? QuietHoursStart, string? QuietHoursEnd, bool ForecastAlertsEnabled, string DefaultQuietHoursStart);
    private record AlertData(Guid Id, string Kind, string Title, string Status, bool MarkedInaccurate);
    private record BudgetData(Guid Id, long LimitCents);
    private record TransferData(BudgetData From, BudgetData To);
    private record BudgetForecastData(Guid BudgetId, long LimitCents, string Status);
    private record UpcomingData(DateOnly Date, string Label, long AmountCents);
    private record IncomeData(bool IsRegular, List<UpcomingData> Upcoming);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Alert Test"));
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

    private async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code;

    private async Task<Guid> MeAsync(string token) => (await SendAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token)).Id;

    private Task<IdData> CashWalletAsync(string token, long balance)
        => SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = balance }, HttpStatusCode.Created);

    /// <summary>Ngưỡng 500.000đ, không có giờ yên lặng (bắt đầu = kết thúc) để test không phụ thuộc giờ chạy.</summary>
    private Task<SettingsData> EnableLowBalanceAsync(string token)
        => SendAsync<SettingsData>(HttpMethod.Put, "/api/v1/me/forecast-settings", token,
            new { lowBalanceThresholdCents = 500_000, quietHoursStart = "00:00", quietHoursEnd = "00:00", forecastAlertsEnabled = true });

    [Fact]
    public async Task Settings_RoundTrip_AndBadQuietHoursAreRejected()
    {
        var token = await NewUserTokenAsync();

        var saved = await SendAsync<SettingsData>(HttpMethod.Put, "/api/v1/me/forecast-settings", token,
            new { lowBalanceThresholdCents = 300_000, quietHoursStart = "23:00", quietHoursEnd = "06:30", forecastAlertsEnabled = true });
        saved.QuietHoursStart.Should().Be("23:00");
        saved.DefaultQuietHoursStart.Should().Be("22:00");
        (await SendAsync<SettingsData>(HttpMethod.Get, "/api/v1/me/forecast-settings", token)).LowBalanceThresholdCents.Should().Be(300_000);

        var bad = await SendRawAsync(HttpMethod.Put, "/api/v1/me/forecast-settings", token, new { quietHoursStart = "7h", quietHoursEnd = "08:00" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LowBalance_LandsInTheInboxOnce_AndCanBeMarkedInaccurate()
    {
        var token = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(token, 1_000_000);
        await SendAsync<IdData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Tiền nhà", counterpartyName = "Chủ nhà", amountCents = 4_000_000, cadence = "Monthly",
            nextDueDate = VietnamTime.Today().AddDays(3), financialAccountId = wallet.Id,
        }, HttpStatusCode.Created);
        await EnableLowBalanceAsync(token);
        var userId = await MeAsync(token);

        for (var run = 0; run < 2; run++)
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IForecastAlertService>().CheckUserAsync(userId);
        }

        var alerts = await SendAsync<List<AlertData>>(HttpMethod.Get, "/api/v1/forecast/alerts", token);
        var low = alerts.Should().ContainSingle(a => a.Kind == "low_balance").Subject;
        low.Status.Should().Be("sent");

        var marked = await SendAsync<AlertData>(HttpMethod.Post, $"/api/v1/forecast/alerts/{low.Id}/feedback", token, new { feedback = "inaccurate" });
        marked.MarkedInaccurate.Should().BeTrue();

        (await SendRawAsync(HttpMethod.Post, $"/api/v1/forecast/alerts/{low.Id}/feedback", token, new { feedback = "great" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stranger = await NewUserTokenAsync();
        (await SendRawAsync(HttpMethod.Post, $"/api/v1/forecast/alerts/{low.Id}/feedback", stranger, new { feedback = "inaccurate" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BudgetTransfer_MovesUnspentLimit_AndRefusesMore()
    {
        var token = await NewUserTokenAsync();
        var categories = await SendAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token);
        var food = await SendAsync<BudgetData>(HttpMethod.Post, "/api/v1/budgets", token,
            new { categoryId = categories.First(c => c.Slug == "food").Id, limitCents = 3_000_000 }, HttpStatusCode.Created);
        var fun = await SendAsync<BudgetData>(HttpMethod.Post, "/api/v1/budgets", token,
            new { categoryId = categories.First(c => c.Slug == "entertainment").Id, limitCents = 1_000_000 }, HttpStatusCode.Created);

        var moved = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/budgets/transfer", token,
            new { fromBudgetId = food.Id, toBudgetId = fun.Id, amountCents = 500_000 });
        moved.From.LimitCents.Should().Be(2_500_000);
        moved.To.LimitCents.Should().Be(1_500_000);

        var tooMuch = await SendRawAsync(HttpMethod.Post, "/api/v1/budgets/transfer", token,
            new { fromBudgetId = food.Id, toBudgetId = fun.Id, amountCents = 9_000_000 });
        tooMuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(tooMuch)).Should().Be("BUDGET_TRANSFER_EXCEEDS_UNSPENT");

        var stranger = await NewUserTokenAsync();
        (await SendRawAsync(HttpMethod.Post, "/api/v1/budgets/transfer", stranger, new { fromBudgetId = food.Id, toBudgetId = fun.Id, amountCents = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var forecast = await SendAsync<List<BudgetForecastData>>(HttpMethod.Get, "/api/v1/forecast/budgets", token);
        forecast.Should().HaveCount(2).And.OnlyContain(b => b.Status == "ok");
    }

    [Fact]
    public async Task DeclaredSalary_ShowsUpOnTheIncomeSchedule()
    {
        var token = await NewUserTokenAsync();
        await SendAsync<IdData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Inflow", name = "Lương", counterpartyName = "Công ty ABC", amountCents = 15_000_000, cadence = "Monthly",
            nextDueDate = VietnamTime.Today().AddDays(5),
        }, HttpStatusCode.Created);

        var income = await SendAsync<IncomeData>(HttpMethod.Get, "/api/v1/forecast/income", token);

        income.IsRegular.Should().BeTrue();
        income.Upcoming.Should().Contain(u => u.Label == "Lương" && u.AmountCents == 15_000_000 && u.Date == VietnamTime.Today().AddDays(5));
    }
}
