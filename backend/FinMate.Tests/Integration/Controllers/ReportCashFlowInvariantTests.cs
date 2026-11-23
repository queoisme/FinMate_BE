using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Bất biến dòng tiền của báo cáo tháng qua API thật (Phase 90, Q16, D1–D3), xem tất cả ví và từng ví:
/// <c>netCents = thu − chi − vayRa + vayVào − quỹRa + quỹVào − chuyểnRa + chuyểnVào + hoànVượtChi + đảoRòng</c>, và <c>netCents</c> bằng đúng thay
/// đổi số dư thật (không gồm số dư ban đầu khi tạo ví). Quỹ nhóm và cặp đảo khác kỳ có ở test đơn vị <c>CashFlowCalculatorTests</c>.
/// </summary>
[Collection("Integration")]
public class ReportCashFlowInvariantTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private string _token = string.Empty;

    public ReportCashFlowInvariantTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthBody(AuthData Data);
    private record AuthData(string AccessToken);

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data");
    }

    private async Task<Guid> WalletAsync(string name, long balance)
        => (await SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", new { accountName = name, accountType = "Cash", initialBalanceCents = balance }))
            .GetProperty("id").GetGuid();

    private async Task<Guid> CategoryAsync(string slug)
        => (await SendAsync(HttpMethod.Get, "/api/v1/categories")).EnumerateArray().First(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();

    private async Task<Guid> TxAsync(Guid wallet, Guid category, long amount, string type = "Debit")
        => (await SendAsync(HttpMethod.Post, "/api/v1/transactions", new
        {
            financialAccountId = wallet, categoryId = category, amountCents = amount, transactionType = type, transactedAt = DateTimeOffset.UtcNow,
        })).GetProperty("id").GetGuid();

    private async Task<long> BalanceAsync(Guid wallet)
        => (await SendAsync(HttpMethod.Get, $"/api/v1/financial-accounts/{wallet}/balance")).GetProperty("balanceCents").GetInt64();

    private static void AssertInvariant(JsonElement summary, string scope)
    {
        var c = summary.GetProperty("cashFlow");
        long F(string name) => c.GetProperty(name).GetInt64();
        F("netCents").Should().Be(
            summary.GetProperty("totalIncomeCents").GetInt64() - summary.GetProperty("totalSpentCents").GetInt64()
            - F("loanOutflowCents") + F("loanInflowCents") - F("groupFundOutflowCents") + F("groupFundInflowCents")
            - F("transferOutflowCents") + F("transferInflowCents") + F("refundSurplusCents") + F("reversalNetCents"),
            scope);
    }

    [Fact]
    public async Task MonthlySummary_TheInvariantHolds_ForAllWalletsAndEachWallet_AndNetIsTheRealBalanceChange()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Cash Flow"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        _token = (await login.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;

        var a = await WalletAsync("Ví A", 10_000_000);
        var b = await WalletAsync("Ví B", 5_000_000);
        var food = await CategoryAsync("food");
        var income = await CategoryAsync("income");
        var loan = await CategoryAsync("loan");

        await TxAsync(a, food, 300_000);
        await TxAsync(a, income, 1_000_000, "Credit");
        await SendAsync(HttpMethod.Post, "/api/v1/transactions/transfer", new { fromAccountId = a, toAccountId = b, amountCents = 2_000_000, transactedAt = DateTimeOffset.UtcNow });
        await TxAsync(b, loan, 500_000);
        await TxAsync(a, loan, 200_000, "Credit");
        var refunded = await TxAsync(a, food, 400_000);
        await SendAsync(HttpMethod.Post, $"/api/v1/transactions/{refunded}/refunds", new { amountCents = 100_000 });
        var reversed = await TxAsync(b, food, 250_000);
        await SendAsync(HttpMethod.Post, $"/api/v1/transactions/{reversed}/refunds", new { relation = "Reversal" });

        var all = await SendAsync(HttpMethod.Get, "/api/v1/reports/monthly-summary");
        AssertInvariant(all, "mọi ví");
        all.GetProperty("cashFlow").GetProperty("netCents").GetInt64().Should().Be(await BalanceAsync(a) + await BalanceAsync(b) - 15_000_000);
        all.GetProperty("cashFlow").GetProperty("transferOutflowCents").GetInt64().Should().Be(0);

        foreach (var (wallet, start, name) in new[] { (a, 10_000_000L, "ví A"), (b, 5_000_000L, "ví B") })
        {
            var one = await SendAsync(HttpMethod.Get, $"/api/v1/reports/monthly-summary?accountId={wallet}");
            AssertInvariant(one, name);
            one.GetProperty("cashFlow").GetProperty("netCents").GetInt64().Should().Be(await BalanceAsync(wallet) - start, name);
        }

        var day = (await SendAsync(HttpMethod.Get, "/api/v1/reports/timeline?limit=50")).EnumerateArray().Single();
        AssertInvariant(day, "ngày của timeline");
    }
}
