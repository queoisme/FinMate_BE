using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Các bước lặp lại của test sổ vay (Phase 90, chép từ bản sổ nợ Phase 86 cũ): một người dùng mới, ví, danh mục hệ thống, giao dịch, gọi API và đọc phần <c>data</c>.</summary>
internal sealed class LoanTestApi(AuthApiFactory factory, HttpClient client)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Token { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }

    private record AuthBody(AuthData Data);
    private record AuthData(string AccessToken);

    public async Task<LoanTestApi> SignUpAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Loan Tester"));
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        Token = (await login.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;
        UserId = await InDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return this;
    }

    public async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Gửi và đòi đúng mã trạng thái; trả phần <c>data</c> (hoặc cả thân khi không có).</summary>
    public async Task<JsonElement> ExpectAsync(HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        var response = await SendAsync(method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        var root = JsonDocument.Parse(text).RootElement;
        return root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null ? data : root;
    }

    public async Task<string> ErrorCodeAsync(HttpMethod method, string url, object? body, HttpStatusCode expected)
        => (await ExpectAsync(method, url, body, expected)).GetProperty("error").GetProperty("code").GetString()!;

    public async Task<Guid> WalletAsync(string name = "VCB", long balance = 10_000_000)
        => (await ExpectAsync(HttpMethod.Post, "/api/v1/financial-accounts", new { accountName = name, accountType = "Cash", initialBalanceCents = balance }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

    public async Task<long> BalanceAsync(Guid wallet)
        => (await ExpectAsync(HttpMethod.Get, $"/api/v1/financial-accounts/{wallet}/balance", null, HttpStatusCode.OK)).GetProperty("balanceCents").GetInt64();

    public async Task<Guid> CategoryAsync(string slug)
        => (await ExpectAsync(HttpMethod.Get, "/api/v1/categories", null, HttpStatusCode.OK)).EnumerateArray()
            .First(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();

    public async Task<Guid> TransactionAsync(Guid wallet, Guid? category, long amount, string type = "Debit", DateTimeOffset? at = null, string? merchant = null)
        => (await ExpectAsync(HttpMethod.Post, "/api/v1/transactions", new
        {
            financialAccountId = wallet, categoryId = category, amountCents = amount, transactionType = type, transactedAt = at ?? DateTimeOffset.UtcNow,
            merchantName = merchant,
        }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    public Task<JsonElement> TransactionDetailAsync(Guid id) => ExpectAsync(HttpMethod.Get, $"/api/v1/transactions/{id}", null, HttpStatusCode.OK);

    public Task<JsonElement> MonthlySummaryAsync() => ExpectAsync(HttpMethod.Get, "/api/v1/reports/monthly-summary", null, HttpStatusCode.OK);
}
