using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Số dư, phần đã chi và phần đã để dành đều được đọc rồi cộng trong tiến trình. Nhiều request cùng lúc không được làm mất khoản nào:
/// request nào đã báo thành công thì tiền của nó phải có mặt. Request hết lượt thử lại được phép trả 409 (client gửi lại), không được phép 500.
/// Mức chạy song song là thật nên số request thành công không cố định: test kiểm tra quan hệ giữa số đó và số tiền.
/// </summary>
[Collection("Integration")]
public class MoneyConcurrencyTests : IClassFixture<AuthApiFactory>
{
    private const int ConcurrentRequests = 12;
    private const long AmountCents = 100_000L;
    private const long InitialBalanceCents = 100_000_000L;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public MoneyConcurrencyTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug, bool IsSystem);
    private record AccountData(Guid AccountId, long BalanceCents);
    private record SummaryItem(Guid BudgetId, long SpentCents);
    private record SummaryData(SummaryItem? TotalBudget, List<SummaryItem> CategoryBudgets);
    private record GoalData(Guid Id, long SavedCents);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Concurrency Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string url, string token, object? body = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private async Task<Guid> NewAccountAsync(string token)
        => (await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token, new
        {
            accountType = "Cash",
            accountName = "Ví tiền mặt",
            initialBalanceCents = InitialBalanceCents,
        })).Id;

    private static int CountSucceeded(IReadOnlyList<HttpResponseMessage> responses, HttpStatusCode success)
    {
        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == success || s == HttpStatusCode.Conflict);
        var succeeded = responses.Count(r => r.StatusCode == success);
        succeeded.Should().BeGreaterThan(0);
        return succeeded;
    }

    [Fact]
    public async Task ConcurrentSpendingLosesNoMoney_InTheWalletOrInTheBudgets()
    {
        var token = await NewUserTokenAsync();
        var accountId = await NewAccountAsync(token);
        var categories = await ReadAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token);
        var foodId = categories.First(c => c.IsSystem && c.Slug == "food").Id;
        await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", token, new { categoryId = foodId, limitCents = 100_000_000L });
        await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", token, new { categoryId = (Guid?)null, limitCents = 100_000_000L });

        var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(_ => SendAsync(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = AmountCents,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        })));

        var succeeded = CountSucceeded(responses, HttpStatusCode.Created);
        var expectedSpent = succeeded * AmountCents;

        var account = await ReadAsync<AccountData>(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", token);
        account.BalanceCents.Should().Be(InitialBalanceCents - expectedSpent);

        var summary = await ReadAsync<SummaryData>(HttpMethod.Get, "/api/v1/budgets", token);
        summary.TotalBudget!.SpentCents.Should().Be(expectedSpent);
        summary.CategoryBudgets.Single().SpentCents.Should().Be(expectedSpent);
    }

    [Fact]
    public async Task ConcurrentContributionsLoseNoMoney()
    {
        var token = await NewUserTokenAsync();
        var goal = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/saving-goals", token, new { name = "Du lịch", targetCents = 100_000_000L });

        var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(_ =>
            SendAsync(HttpMethod.Post, $"/api/v1/saving-goals/{goal.Id}/contribute", token, new { amountCents = AmountCents })));

        var succeeded = CountSucceeded(responses, HttpStatusCode.OK);
        var goals = await ReadAsync<List<GoalData>>(HttpMethod.Get, "/api/v1/saving-goals", token);
        goals.Single(g => g.Id == goal.Id).SavedCents.Should().Be(succeeded * AmountCents);
    }

    [Fact]
    public async Task AnOutOfDateWriteOutsideTheRetriedPathsAnswers409_Not500()
    {
        var token = await NewUserTokenAsync();
        var accountId = await NewAccountAsync(token);

        var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(i =>
            SendAsync(HttpMethod.Patch, $"/api/v1/financial-accounts/{accountId}", token, new { accountName = $"Ví {i}" })));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
    }
}
