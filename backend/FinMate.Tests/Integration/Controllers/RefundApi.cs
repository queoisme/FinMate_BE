using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Lời gọi HTTP và đọc DB dùng chung cho các test tích hợp hoàn tiền / đảo giao dịch (Phase 81).</summary>
internal sealed class RefundApi
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<FinMate.API.Program> _factory;
    private readonly HttpClient _client;

    public RefundApi(WebApplicationFactory<FinMate.API.Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public HttpClient Client => _client;

    public sealed record Session(string Token, Guid Account, Guid Shopping, Guid Food, Guid Income);

    public record Envelope<T>(bool Success, T Data);
    public record ErrorEnvelope(bool Success, ErrorData Error);
    public record ErrorData(string Code);
    public record IdData(Guid Id);
    public record AuthData(string AccessToken);
    public record CategoryData(Guid Id, string Slug);

    public record TransactionData(
        Guid Id, long AmountCents, string TransactionType, string Status, Guid? CategoryId, Guid FinancialAccountId, Guid? GroupId,
        string? Relation, Guid? RelatedTransactionId, bool IsReversed, long? RefundedCents, long? RemainingRefundableCents,
        DateTimeOffset TransactedAt, List<SuggestionData>? SuggestedRelatedTransactions);

    public record SuggestionData(Guid TransactionId, long AmountCents, long RemainingRefundableCents, string? MerchantName);

    public async Task<Session> StartAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Refund Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var token = (await login.Content.ReadFromJsonAsync<Envelope<AuthData>>(JsonOptions))!.Data.AccessToken;
        var account = await CreateAccountAsync(token);
        var categories = await SendAsync(HttpMethod.Get, "/api/v1/categories", token);
        var list = (await categories.Content.ReadFromJsonAsync<Envelope<List<CategoryData>>>(JsonOptions))!.Data;
        return new Session(token, account, Id(list, "shopping"), Id(list, "food"), Id(list, "income"));
    }

    private static Guid Id(List<CategoryData> list, string slug) => list.First(c => c.Slug == slug).Id;

    public async Task<Guid> CreateAccountAsync(string token, string name = "Ví tiền mặt", long balance = 5_000_000)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", token, new CreateFinancialAccountRequest(name, AccountType.Cash, null, balance));
        return (await response.Content.ReadFromJsonAsync<Envelope<IdData>>(JsonOptions))!.Data.Id;
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    public async Task<TransactionData> SpendAsync(Session s, long amount, DateTimeOffset? at = null, Guid? category = null, string merchant = "Shopee - Giày")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/transactions", s.Token,
            new CreateManualTransactionRequest(s.Account, category ?? s.Shopping, amount, TransactionType.Debit, at ?? DateTimeOffset.UtcNow, merchant, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await DataAsync(response);
    }

    public async Task<TransactionData> EarnAsync(Session s, long amount, DateTimeOffset? at = null, string merchant = "Shopee")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/transactions", s.Token,
            new CreateManualTransactionRequest(s.Account, s.Income, amount, TransactionType.Credit, at ?? DateTimeOffset.UtcNow, merchant, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await DataAsync(response);
    }

    public Task<HttpResponseMessage> RefundAsync(Session s, Guid original, object? body = null)
        => SendAsync(HttpMethod.Post, $"/api/v1/transactions/{original}/refunds", s.Token, body ?? new { });

    public Task<HttpResponseMessage> LinkAsync(Session s, Guid transaction, Guid? original, string relation = "Refund", Guid? categoryId = null)
        => SendAsync(HttpMethod.Put, $"/api/v1/transactions/{transaction}/related", s.Token, new { relatedTransactionId = original, relation, categoryId });

    public Task<HttpResponseMessage> UnlinkAsync(Session s, Guid transaction)
        => SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{transaction}/related", s.Token);

    public async Task<TransactionData> GetAsync(Session s, Guid id)
        => await DataAsync(await SendAsync(HttpMethod.Get, $"/api/v1/transactions/{id}", s.Token));

    public async Task<Guid> BudgetAsync(Session s, long limit, Guid? category = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/budgets", s.Token, new CreateBudgetRequest(category ?? s.Shopping, limit));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Envelope<IdData>>(JsonOptions))!.Data.Id;
    }

    public static async Task<TransactionData> DataAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<Envelope<TransactionData>>(JsonOptions))!.Data;

    public static async Task<string> ErrorAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions))!.Error.Code;

    public async Task<T> WithDbAsync<T>(Func<FinMateDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    /// <summary>Một nháp tiền vào như đường thông báo tạo ra (chưa tác động số dư hay ngân sách), chèn thẳng vào DB.</summary>
    public Task<Guid> DraftCreditAsync(Session s, long amount, string merchant = "Shopee")
        => WithDbAsync(async db =>
        {
            var userId = await db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync();
            var now = DateTimeOffset.UtcNow;
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = s.Account, CategoryId = s.Income, AmountCents = amount,
                TransactionType = TransactionType.Credit, Source = TransactionSource.Notification, Status = TransactionStatus.Draft,
                MerchantName = merchant, TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });

    public Task<HttpResponseMessage> ConfirmAsync(Session s, Guid draft, object? body = null)
        => SendAsync(HttpMethod.Post, $"/api/v1/transactions/{draft}/confirm", s.Token, body ?? new { });

    public Task<long> BalanceAsync(Guid account)
        => WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == account).Select(a => a.BalanceCents).SingleAsync());

    public Task<BudgetPeriod> PeriodAsync(Guid budget, DateTimeOffset? at = null)
        => WithDbAsync(db =>
        {
            var query = db.BudgetPeriods.AsNoTracking().Where(p => p.BudgetId == budget);
            return at is { } when
                ? query.SingleAsync(p => p.PeriodStart <= when && p.PeriodEnd > when)
                : query.OrderByDescending(p => p.PeriodStart).FirstAsync();
        });
}
