using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Đường ghi mới của sổ vay trên Postgres thật (Phase 90, Task 5): một lần lưu cho dòng, khoản (<c>xmin</c>), ví và giao dịch; trả kèm lãi (S1)
/// thành giao dịch lãi tính vào chi tiêu thật (Q16); xoá khoản là xoá mềm (Q4). Endpoint mới có test riêng ở Task 6.
/// </summary>
[Collection("Integration")]
public class LoanWritesTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private string _token = string.Empty;

    public LoanWritesTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthBody(AuthData Data);
    private record AuthData(string AccessToken);

    private async Task<(HttpStatusCode Status, JsonElement Data)> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length == 0)
        {
            return (response.StatusCode, default);
        }

        var root = JsonDocument.Parse(text).RootElement;
        return (response.StatusCode, response.IsSuccessStatusCode && root.TryGetProperty("data", out var d) ? d : root);
    }

    private async Task LoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Loan Writes"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        _token = (await login.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<Guid> WalletAsync(long balance)
        => (await SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", new { accountName = "Ví", accountType = "Cash", initialBalanceCents = balance }))
            .Data.GetProperty("id").GetGuid();

    private async Task<long> BalanceAsync(Guid wallet)
        => (await SendAsync(HttpMethod.Get, $"/api/v1/financial-accounts/{wallet}/balance")).Data.GetProperty("balanceCents").GetInt64();

    [Fact]
    public async Task ScenarioI_Repaying2060000_At12Percent_After90Days_SettlesAndBooks60000AsRealSpending()
    {
        await LoginAsync();
        var wallet = await WalletAsync(10_000_000);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var (created, loan) = await SendAsync(HttpMethod.Post, "/api/v1/loans", new
        {
            direction = "borrow", title = "Vay", counterparty = "Người", principalCents = 2_000_000, interestRatePercentYear = 12, startDate = today.AddDays(-90),
        });
        created.Should().Be(HttpStatusCode.Created);
        var id = loan.GetProperty("id").GetGuid();
        loan.GetProperty("paidCents").GetInt64().Should().Be(0, "dòng giải ngân không phải lần trả");

        var (status, repaid) = await SendAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 2_060_000, financialAccountId = wallet });

        status.Should().Be(HttpStatusCode.OK, repaid.ToString());
        (repaid.GetProperty("paidCents").GetInt64(), repaid.GetProperty("remainingCents").GetInt64()).Should().Be((2_000_000L, 0L));
        (await BalanceAsync(wallet)).Should().Be(10_000_000 - 2_060_000);
        var month = (await SendAsync(HttpMethod.Get, "/api/v1/reports/monthly-summary")).Data;
        month.GetProperty("totalSpentCents").GetInt64().Should().Be(60_000, "lãi vay là chi tiêu thật (S1, Q16)");
        month.GetProperty("cashFlow").GetProperty("loanOutflowCents").GetInt64().Should().Be(2_000_000);
        month.GetProperty("cashFlow").GetProperty("netCents").GetInt64().Should().Be(-2_060_000);
    }

    [Fact]
    public async Task Repaying_OverTheRemaining_WithoutInterest_Is422_AndNothingMoves()
    {
        await LoginAsync();
        var wallet = await WalletAsync(5_000_000);
        var id = (await SendAsync(HttpMethod.Post, "/api/v1/loans", new { direction = "lend", title = "Cho vay", counterparty = "Người", principalCents = 1_000_000 }))
            .Data.GetProperty("id").GetGuid();

        var (status, body) = await SendAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 1_000_001, financialAccountId = wallet });

        status.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.ToString().Should().Contain("LOAN_REPAYMENT_EXCEEDS_REMAINING");
        (await BalanceAsync(wallet)).Should().Be(5_000_000);
    }

    [Fact]
    public async Task Deleting_IsSoft_TheLinkedTransactionStays_AndTheListNoLongerShowsTheLoan()
    {
        await LoginAsync();
        var wallet = await WalletAsync(5_000_000);
        var id = (await SendAsync(HttpMethod.Post, "/api/v1/loans", new { direction = "lend", title = "Cho vay", counterparty = "Người", principalCents = 1_000_000 }))
            .Data.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 400_000, financialAccountId = wallet });

        (await SendAsync(HttpMethod.Delete, $"/api/v1/loans/{id}")).Status.Should().Be(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Get, "/api/v1/loans")).Data.EnumerateArray().Should().BeEmpty();
        (await BalanceAsync(wallet)).Should().Be(5_400_000, "giao dịch thu nợ đã ghi vẫn ở lại");
    }
}
