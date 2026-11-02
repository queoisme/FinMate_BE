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
/// Dự báo tài chính đợt 1 qua API thật: khoản định kỳ thêm tay rơi đúng ngày trên đường số dư và bị trừ khỏi số tiền có thể chi, mọi dữ liệu
/// tách theo người dùng, và job phát hiện tìm ra thuê bao từ giao dịch thật trong DB.
/// </summary>
[Collection("Integration")]
public class ForecastControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ForecastControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record ItemData(Guid Id, string Status, string Source, string? CounterpartyKey, long AmountCents);
    private record EventData(string Kind, string Label, long AmountCents);
    private record DayData(DateOnly Date, long ExpectedBalanceCents, List<EventData> Events);
    private record CashflowData(long StartBalanceCents, long EndBalanceCents, List<DayData> Days);
    private record LineData(DateOnly Date, string Label, long AmountCents);
    private record SpendableData(long BalanceCents, long UpcomingBillsCents, long ReserveCents, long SpendableCents, List<LineData> Bills);
    private record ExclusionData(Guid TransactionId, bool ExcludeFromForecast, string? Reason);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

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

    private async Task<Guid> CashWalletAsync(string token, long balance)
        => (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = balance }, HttpStatusCode.Created)).Id;

    private Task<ItemData> RentDueTodayAsync(string token, Guid walletId)
        => SendAsync<ItemData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow",
            name = "Tiền nhà",
            counterpartyName = "Chủ nhà",
            amountCents = 4_000_000,
            cadence = "Monthly",
            nextDueDate = VietnamTime.Today(),
            financialAccountId = walletId,
        }, HttpStatusCode.Created);

    [Fact]
    public async Task Forecast_WithoutAToken_IsUnauthorized()
        => (await _client.GetAsync("/api/v1/forecast/spendable")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ManualRent_LandsOnTheCashflow_AndComesOffWhatCanBeSpent()
    {
        var token = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(token, 10_000_000);
        var rent = await RentDueTodayAsync(token, wallet);
        rent.Status.Should().Be("active");
        rent.Source.Should().Be("manual");

        var cashflow = await SendAsync<CashflowData>(HttpMethod.Get, "/api/v1/forecast/cashflow", token);
        cashflow.StartBalanceCents.Should().Be(10_000_000);
        cashflow.Days[0].Events.Should().ContainSingle(e => e.Label == "Tiền nhà" && e.AmountCents == -4_000_000);
        cashflow.Days[0].ExpectedBalanceCents.Should().Be(6_000_000, "user mới chưa có thói quen chi tiêu nên chỉ trừ tiền nhà");

        var spendable = await SendAsync<SpendableData>(HttpMethod.Get, "/api/v1/forecast/spendable", token);
        spendable.UpcomingBillsCents.Should().Be(4_000_000);
        spendable.ReserveCents.Should().Be(0, "chưa khai thu nhập thì dự phòng mặc định là 0");
        spendable.SpendableCents.Should().Be(6_000_000);
    }

    [Fact]
    public async Task SameCounterpartyTwice_IsAConflict()
    {
        var token = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(token, 0);
        await RentDueTodayAsync(token, wallet);

        var again = await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Nhà", counterpartyName = "CHỦ NHÀ", amountCents = 1, cadence = "Monthly", nextDueDate = VietnamTime.Today(),
        });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("RECURRING_ITEM_DUPLICATE");
    }

    [Fact]
    public async Task AnotherUsersData_IsNotFound()
    {
        var owner = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(owner, 1_000_000);
        var rent = await RentDueTodayAsync(owner, wallet);
        var stranger = await NewUserTokenAsync();

        (await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{rent.Id}/pause", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Get, $"/api/v1/forecast/cashflow?accountId={wallet}", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Patch, $"/api/v1/financial-accounts/{wallet}/forecast-inclusion", stranger, new { included = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExcludedWallet_DropsOutOfTheTotal()
    {
        var token = await NewUserTokenAsync();
        await CashWalletAsync(token, 2_000_000);
        var savings = await CashWalletAsync(token, 50_000_000);

        await SendAsync<object>(HttpMethod.Patch, $"/api/v1/financial-accounts/{savings}/forecast-inclusion", token, new { included = false });

        (await SendAsync<SpendableData>(HttpMethod.Get, "/api/v1/forecast/spendable", token)).BalanceCents.Should().Be(2_000_000);
    }

    [Fact]
    public async Task MarkingATransactionOneOff_IsStoredAndReadable()
    {
        var token = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(token, 5_000_000);
        var tx = await SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = wallet, amountCents = 2_000_000, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, merchantName = "Tiệc cưới",
        }, HttpStatusCode.Created);

        var result = await SendAsync<ExclusionData>(HttpMethod.Patch, $"/api/v1/transactions/{tx.Id}/forecast-exclusion", token,
            new { excluded = true, reason = "OneOff" });

        result.ExcludeFromForecast.Should().BeTrue();
        result.Reason.Should().Be("one_off");
    }

    [Fact]
    public async Task DetectionJob_FindsAMonthlySubscriptionInRealTransactions()
    {
        var token = await NewUserTokenAsync();
        var wallet = await CashWalletAsync(token, 5_000_000);
        foreach (var daysAgo in new[] { 61, 31, 1 })
        {
            await SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
            {
                financialAccountId = wallet, amountCents = 260_000, transactionType = "Debit",
                transactedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo), merchantName = "Netflix",
            }, HttpStatusCode.Created);
        }

        var me = await SendAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token);
        using (var scope = _factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IRecurringDetectionService>().RunForUserAsync(me.Id)).Should().Be(1);
        }

        var items = await SendAsync<List<ItemData>>(HttpMethod.Get, "/api/v1/recurring-items", token);
        items.Should().ContainSingle(i => i.CounterpartyKey == "netflix" && i.Status == "active" && i.Source == "detected" && i.AmountCents == 260_000);
    }
}
