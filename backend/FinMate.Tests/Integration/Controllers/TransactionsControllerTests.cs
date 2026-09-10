using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class TransactionsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TransactionsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record FinancialAccountBody(bool Success, FinancialAccountData Data);
    private record FinancialAccountData(Guid Id, long BalanceCents);
    private record TransactionBody(bool Success, TransactionData Data);
    private record TransactionData(Guid Id, long AmountCents, string Status, string TransactionType);
    private record AnalysisBody(bool Success, AnalysisData Data);
    private record AnalysisData(Guid NotificationLogId, string Status, Guid? DraftTransactionId);
    private record TransactionListBody(bool Success, List<TransactionData> Data, ApiMetaBody? Meta);
    private record ApiMetaBody(string? Cursor);
    private record ParsedBody(bool Success, ParsedData Data);
    private record ParsedData(long? AmountCents, string? TransactionType, string? MerchantName, string? CategorySlug);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Transaction Test User"));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return loginBody!.Data.AccessToken;
    }

    private HttpRequestMessage AuthedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<Guid> GetSeededProviderIdAsync(string providerKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var provider = await db.ProviderConfigs.FirstAsync(p => p.ProviderKey == providerKey);
        return provider.Id;
    }

    private async Task<(Guid Id, long BalanceCents)> CreateCashAccountAsync(string accessToken, long initialBalance = 1_000_000)
    {
        var request = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        request.Content = JsonContent.Create(new
        {
            accountName = "Ví tiền mặt",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = initialBalance,
        });
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions);
        return (body!.Data.Id, body.Data.BalanceCents);
    }

    [Fact]
    public async Task CreateUpdateDeleteManualTransaction_AdjustsAccountBalanceEachStep()
    {
        var accessToken = await RegisterAndLoginAsync();
        var (accountId, _) = await CreateCashAccountAsync(accessToken, 1_000_000);

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/transactions", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            financialAccountId = accountId,
            categoryId = (Guid?)null,
            amountCents = 100_000,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = "Coffee",
            description = (string?)null,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResponse.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions);
        var transactionId = createBody!.Data.Id;

        var balanceAfterCreate = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        (await balanceAfterCreate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("balanceCents").GetInt64().Should().Be(900_000);

        var updateRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/transactions/{transactionId}", accessToken);
        updateRequest.Content = JsonContent.Create(new
        {
            financialAccountId = accountId,
            categoryId = (Guid?)null,
            amountCents = 150_000,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = "Coffee",
            description = (string?)null,
        });
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var balanceAfterUpdate = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        (await balanceAfterUpdate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("balanceCents").GetInt64().Should().Be(850_000);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/transactions/{transactionId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var balanceAfterDelete = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        (await balanceAfterDelete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("balanceCents").GetInt64().Should().Be(1_000_000);
    }

    [Fact]
    public async Task NotificationDraft_Confirm_UpdatesAccountBalance()
    {
        var accessToken = await RegisterAndLoginAsync();
        var providerId = await GetSeededProviderIdAsync("zalopay");

        var createAccountRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createAccountRequest.Content = JsonContent.Create(new
        {
            accountName = "ZaloPay",
            accountType = "EWallet",
            providerConfigId = providerId,
            initialBalanceCents = 500_000,
        });
        var accountResponse = await _client.SendAsync(createAccountRequest);
        var accountId = (await accountResponse.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions))!.Data.Id;

        FakeAIServiceClient.SetScenario("vn.com.vng.zalopay", new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.95),
            new ExtractionResult(60_000, "debit", "Shopee", "Thanh toan don hang", DateTimeOffset.UtcNow, 440_000, 0.9),
            new CategorizationResult("shopping", 0.85),
            new DuplicateResult(false, null),
            new ModelVersions("1.0", "1.0", "1.0"),
            80));

        var analyzeRequest = AuthedRequest(HttpMethod.Post, "/api/v1/notifications/analyze", accessToken);
        analyzeRequest.Content = JsonContent.Create(new
        {
            packageName = "vn.com.vng.zalopay",
            notificationTitle = "Thanh toan",
            notificationBody = "Ban da thanh toan 60,000d cho Shopee",
            receivedAt = DateTimeOffset.UtcNow,
        });
        var analyzeResponse = await _client.SendAsync(analyzeRequest);
        var analyzeBody = await analyzeResponse.Content.ReadFromJsonAsync<AnalysisBody>(JsonOptions);
        var draftId = analyzeBody!.Data.DraftTransactionId!.Value;

        var draftDetail = await _client.SendAsync(AuthedRequest(HttpMethod.Get, $"/api/v1/transactions/{draftId}", accessToken));
        var draftBody = await draftDetail.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions);
        draftBody!.Data.Status.Should().Be("Draft");

        var confirmResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Post, $"/api/v1/transactions/{draftId}/confirm", accessToken));
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmBody = await confirmResponse.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions);
        confirmBody!.Data.Status.Should().Be("Confirmed");

        var balanceResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        (await balanceResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("balanceCents").GetInt64().Should().Be(440_000);

        var secondConfirm = await _client.SendAsync(AuthedRequest(HttpMethod.Post, $"/api/v1/transactions/{draftId}/confirm", accessToken));
        secondConfirm.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetList_CursorPagination_ReturnsAllItemsAcrossPagesWithoutDuplicates()
    {
        var accessToken = await RegisterAndLoginAsync();
        var (accountId, _) = await CreateCashAccountAsync(accessToken, 10_000_000);

        for (var i = 0; i < 5; i++)
        {
            var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/transactions", accessToken);
            createRequest.Content = JsonContent.Create(new
            {
                financialAccountId = accountId,
                categoryId = (Guid?)null,
                amountCents = 1_000 * (i + 1),
                transactionType = "Debit",
                transactedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                merchantName = (string?)null,
                description = (string?)null,
            });
            (await _client.SendAsync(createRequest)).EnsureSuccessStatusCode();
        }

        var collected = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/api/v1/transactions?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, url, accessToken));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<TransactionListBody>(JsonOptions);
            collected.AddRange(body!.Data.Select(t => t.Id));
            cursor = body.Meta?.Cursor;
        } while (cursor is not null);

        collected.Should().HaveCount(5);
        collected.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task OtherUsersTransaction_GetUpdateDeleteConfirm_AllReturnNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var strangerToken = await RegisterAndLoginAsync();
        var (accountId, _) = await CreateCashAccountAsync(ownerToken, 1_000_000);

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/transactions", ownerToken);
        createRequest.Content = JsonContent.Create(new
        {
            financialAccountId = accountId,
            categoryId = (Guid?)null,
            amountCents = 10_000,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = (string?)null,
            description = (string?)null,
        });
        var createResponse = await _client.SendAsync(createRequest);
        var transactionId = (await createResponse.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Id;

        (await _client.SendAsync(AuthedRequest(HttpMethod.Get, $"/api/v1/transactions/{transactionId}", strangerToken)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var strangerUpdate = AuthedRequest(HttpMethod.Patch, $"/api/v1/transactions/{transactionId}", strangerToken);
        strangerUpdate.Content = JsonContent.Create(new
        {
            financialAccountId = accountId,
            categoryId = (Guid?)null,
            amountCents = 1,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = (string?)null,
            description = (string?)null,
        });
        (await _client.SendAsync(strangerUpdate)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.SendAsync(AuthedRequest(HttpMethod.Post, $"/api/v1/transactions/{transactionId}/confirm", strangerToken)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/api/v1/transactions/{transactionId}", strangerToken)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Parse_ReturnsFieldsFromAIService()
    {
        var accessToken = await RegisterAndLoginAsync();

        FakeAIServiceClient.SetScenario("manual_entry", new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.9),
            new ExtractionResult(50_000, "debit", "Phở 24", null, DateTimeOffset.UtcNow, null, 0.8),
            new CategorizationResult("food", 0.8),
            new DuplicateResult(false, null),
            new ModelVersions(null, null, null),
            30));

        var request = AuthedRequest(HttpMethod.Post, "/api/v1/transactions/parse", accessToken);
        request.Content = JsonContent.Create(new { text = "ăn phở 50k" });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ParsedBody>(JsonOptions);
        body!.Data.AmountCents.Should().Be(50_000);
        body.Data.CategorySlug.Should().Be("food");
    }
}
