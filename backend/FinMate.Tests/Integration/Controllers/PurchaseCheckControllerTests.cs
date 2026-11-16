using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// "Mua món này có ổn không?" qua API thật: ví dụ lương/tiền nhà của đặc tả cho kết quả đúng, đánh giá chỉ đọc (không đổi số dư),
/// dữ liệu tách theo người dùng và input sai bị từ chối.
/// </summary>
[Collection("Integration")]
public class PurchaseCheckControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Url = "/api/v1/forecast/purchase-check";

    private readonly HttpClient _client;

    public PurchaseCheckControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CashflowData(long StartBalanceCents);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record PeriodData(DateOnly From, DateOnly To, string Basis);
    private record PointData(DateOnly Day, long BalanceCents);
    private record WarningData(string Code);
    private record LineData(string Label, long AmountCents, string Source);
    private record BudgetData(string Status, long? LimitCents, long? ProjectedAfterCents);
    private record PlanData(string Status);
    private record PurchaseData(
        string Status, string Verdict, string Summary, PeriodData Period, PointData? LowestBefore, PointData? LowestAfter,
        long ShortfallCents, long? MaxAffordableCents, DateOnly? EarliestSafeDate, List<WarningData> Warnings, List<LineData> Lines,
        BudgetData Budget, PlanData Plan);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Purchase Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

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

    private async Task<Guid> WalletAsync(string token, long balance)
        => (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tiền mặt", accountType = "Cash", initialBalanceCents = balance }, HttpStatusCode.Created)).Id;

    private Task<IdData> RecurringAsync(string token, Guid wallet, string direction, string name, long amount, int dueInDays)
        => SendAsync<IdData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction,
            name,
            counterpartyName = name,
            amountCents = amount,
            cadence = "Monthly",
            nextDueDate = VietnamTime.Today().AddDays(dueInDays),
            financialAccountId = wallet,
        }, HttpStatusCode.Created);

    /// <summary>Ví dụ của đặc tả (thu nhỏ): 5.000.000đ, tiền nhà 3.000.000đ sau 2 ngày, lương 10.000.000đ sau 5 ngày.</summary>
    private async Task<(string Token, Guid Wallet)> RentThenPaydayAsync()
    {
        var token = await NewUserTokenAsync();
        var wallet = await WalletAsync(token, 5_000_000);
        await RecurringAsync(token, wallet, "Outflow", "Tiền nhà", 3_000_000, 2);
        await RecurringAsync(token, wallet, "Inflow", "Lương", 10_000_000, 5);
        return (token, wallet);
    }

    [Fact]
    public async Task WithoutAToken_IsUnauthorized()
        => (await SendRawAsync(HttpMethod.Post, Url, null, new { amountCents = 100_000 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task PurchaseThatEmptiesTheWalletBeforePayday_IsAShortfall_WithAlternatives()
    {
        var (token, _) = await RentThenPaydayAsync();
        var today = VietnamTime.Today();

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 3_000_000, item = "Quần" });

        result.Verdict.Should().Be(PurchaseVerdicts.ShortfallRisk);
        result.Period.Basis.Should().Be(PurchasePeriodBases.NextIncome);
        result.Period.To.Should().Be(today.AddDays(4), "kỳ tính tới ngày trước khi nhận lương");
        result.LowestBefore.Should().Be(new PointData(today.AddDays(2), 2_000_000));
        result.LowestAfter.Should().Be(new PointData(today.AddDays(2), -1_000_000));
        result.ShortfallCents.Should().Be(1_000_000);
        result.MaxAffordableCents.Should().Be(2_000_000);
        result.EarliestSafeDate.Should().Be(today.AddDays(5), "ngày nhận lương là ngày sớm nhất mua không thiếu tiền");
        result.Summary.Should().Contain("Quần").And.Contain("thiếu");
    }

    [Fact]
    public async Task ExplanationTable_ListsEveryInputWithItsSource()
    {
        var (token, _) = await RentThenPaydayAsync();

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 500_000, item = "Giày" });

        result.Lines.Should().Contain(l => l.Label == "Số dư hiện tại" && l.AmountCents == 5_000_000 && l.Source == PurchaseSources.Confirmed);
        result.Lines.Should().Contain(l => l.Label == "Tiền nhà" && l.AmountCents == -3_000_000 && l.Source == PurchaseSources.UserEntered, "khoản thêm tay là do người dùng nhập");
        result.Lines.Should().Contain(l => l.Label == "Giày" && l.AmountCents == -500_000 && l.Source == PurchaseSources.UserEntered);
    }

    [Fact]
    public async Task NewUserWithLittleHistory_IsPartial_SoASafeLookingPurchaseIsNeverCalledFine()
    {
        var (token, _) = await RentThenPaydayAsync();

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 100_000 });

        result.Status.Should().Be(PurchaseStatuses.Partial);
        result.Verdict.Should().Be(PurchaseVerdicts.InsufficientData);
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.LowConfidence);
    }

    [Fact]
    public async Task Checking_NeverChangesTheBalanceOrCreatesAnything()
    {
        var (token, _) = await RentThenPaydayAsync();

        await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 3_000_000 });
        var after = await SendAsync<CashflowData>(HttpMethod.Get, "/api/v1/forecast/cashflow", token);

        after.StartBalanceCents.Should().Be(5_000_000);
    }

    [Fact]
    public async Task WithoutAnyWallet_IsInsufficientData_NotAMadeUpAnswer()
    {
        var token = await NewUserTokenAsync();

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 100_000 });

        result.Status.Should().Be(PurchaseStatuses.InsufficientData);
        result.Verdict.Should().Be(PurchaseVerdicts.InsufficientData);
        result.LowestAfter.Should().BeNull();
    }

    [Fact]
    public async Task WithoutKnownIncome_AUserChosenHorizonSetsThePeriod()
    {
        var token = await NewUserTokenAsync();
        await WalletAsync(token, 5_000_000);

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 1_000_000, horizonDays = 7 });

        result.Period.Basis.Should().Be(PurchasePeriodBases.UserChosen);
        result.Period.To.Should().Be(VietnamTime.Today().AddDays(7));
    }

    [Fact]
    public async Task OverallBudget_IsComparedSeparately_AndNeverSubtractedFromTheCash()
    {
        var token = await NewUserTokenAsync();
        await WalletAsync(token, 50_000_000);
        await SendAsync<object>(HttpMethod.Post, "/api/v1/budgets", token, new { categoryId = (Guid?)null, limitCents = 1_000_000 }, HttpStatusCode.Created);

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 1_500_000, horizonDays = 7 });

        result.Budget.Status.Should().Be(PurchaseImpactStatuses.Over);
        result.Budget.LimitCents.Should().Be(1_000_000);
        result.Budget.ProjectedAfterCents.Should().BeGreaterThanOrEqualTo(1_500_000);
        result.ShortfallCents.Should().Be(0, "vượt ngân sách không phải là thiếu tiền mặt");
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.BudgetOver);
    }

    [Fact]
    public async Task NoBudgetAndNoIncome_AreReportedAsUnknown_NotAsZero()
    {
        var token = await NewUserTokenAsync();
        await WalletAsync(token, 5_000_000);

        var result = await SendAsync<PurchaseData>(HttpMethod.Post, Url, token, new { amountCents = 100_000, horizonDays = 7, categorySlug = "shopping" });

        result.Budget.Status.Should().Be(PurchaseImpactStatuses.Unknown);
        result.Plan.Status.Should().Be(PurchaseImpactStatuses.Unknown);
    }

    [Fact]
    public async Task AnotherUsersWallet_IsNotFound()
    {
        var owner = await NewUserTokenAsync();
        var wallet = await WalletAsync(owner, 1_000_000);
        var stranger = await NewUserTokenAsync();

        var response = await SendRawAsync(HttpMethod.Post, Url, stranger, new { amountCents = 100_000, accountId = wallet });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_000_000_001)]
    public async Task InvalidPrice_IsRejected(long amount)
    {
        var token = await NewUserTokenAsync();

        var response = await SendRawAsync(HttpMethod.Post, Url, token, new { amountCents = amount });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(ForecastErrorCodes.PurchaseInvalid);
    }

    [Fact]
    public async Task PastDateOrBadHorizon_IsRejected()
    {
        var token = await NewUserTokenAsync();

        var past = await SendRawAsync(HttpMethod.Post, Url, token, new { amountCents = 1_000, purchaseDate = VietnamTime.Today().AddDays(-1) });
        var horizon = await SendRawAsync(HttpMethod.Post, Url, token, new { amountCents = 1_000, horizonDays = 0 });

        past.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        horizon.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
