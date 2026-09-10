using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class FinancialAccountsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FinancialAccountsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> GetSeededProviderIdAsync(string providerKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var provider = await db.ProviderConfigs.FirstAsync(p => p.ProviderKey == providerKey);
        return provider.Id;
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record FinancialAccountBody(bool Success, FinancialAccountData Data);
    private record FinancialAccountData(Guid Id, string AccountType, string AccountName, bool IsMonitored, long BalanceCents);
    private record AccountBalanceBody(bool Success, AccountBalanceData Data);
    private record AccountBalanceData(Guid AccountId, long BalanceCents);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Financial Account Test User"));

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

    [Fact]
    public async Task CreateListToggleBalanceDelete_CashAccount_FullFlow_Succeeds()
    {
        var accessToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "Ví tiền mặt",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = 100_000,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResponse.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions);
        var accountId = createBody!.Data.Id;
        createBody.Data.BalanceCents.Should().Be(100_000);

        var listResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/financial-accounts", accessToken));
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var toggleRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/financial-accounts/{accountId}/monitoring", accessToken);
        toggleRequest.Content = JsonContent.Create(new { isMonitored = false });
        var toggleResponse = await _client.SendAsync(toggleRequest);
        toggleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var toggleBody = await toggleResponse.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions);
        toggleBody!.Data.IsMonitored.Should().BeFalse();

        var balanceResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        balanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceBody = await balanceResponse.Content.ReadFromJsonAsync<AccountBalanceBody>(JsonOptions);
        balanceBody!.Data.BalanceCents.Should().Be(100_000);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/financial-accounts/{accountId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var balanceAfterDeleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", accessToken));
        balanceAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateBankAccount_DuplicatePackageNameForSameUser_ReturnsConflict()
    {
        var accessToken = await RegisterAndLoginAsync();
        var providerId = await GetSeededProviderIdAsync("mb_bank");

        var firstRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        firstRequest.Content = JsonContent.Create(new
        {
            accountName = "MB Bank chính",
            accountType = "Bank",
            providerConfigId = providerId,
            initialBalanceCents = 0,
        });
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        secondRequest.Content = JsonContent.Create(new
        {
            accountName = "MB Bank phụ",
            accountType = "Bank",
            providerConfigId = providerId,
            initialBalanceCents = 0,
        });
        var secondResponse = await _client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateBankAccount_UnknownProvider_ReturnsUnprocessableEntity()
    {
        var accessToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "Tài khoản ngân hàng",
            accountType = "Bank",
            providerConfigId = Guid.NewGuid(),
            initialBalanceCents = 0,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateAccount_EmptyAccountName_ReturnsBadRequest()
    {
        var accessToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = 0,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAccount_NegativeInitialBalance_ReturnsBadRequest()
    {
        var accessToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", accessToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "Ví âm",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = -1000,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // AGENTS.md §3.2 / CONVENTIONS.md §6.2: repository lookups must filter by userId so one
    // user can never reach another user's resource by guessing/observing its id.
    [Fact]
    public async Task OtherUsersAccount_GetUpdateToggleDelete_AllReturnNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var strangerToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", ownerToken);
        createRequest.Content = JsonContent.Create(new
        {
            accountName = "Ví riêng tư",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = 100_000,
        });
        var createResponse = await _client.SendAsync(createRequest);
        var createBody = await createResponse.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions);
        var accountId = createBody!.Data.Id;

        var balanceResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", strangerToken));
        balanceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updateRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/financial-accounts/{accountId}", strangerToken);
        updateRequest.Content = JsonContent.Create(new { accountName = "Chiếm đoạt" });
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var toggleRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/financial-accounts/{accountId}/monitoring", strangerToken);
        toggleRequest.Content = JsonContent.Create(new { isMonitored = false });
        var toggleResponse = await _client.SendAsync(toggleRequest);
        toggleResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/financial-accounts/{accountId}", strangerToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The owner can still reach it — proves the 404s above were about ownership, not a broken id.
        var ownerBalanceResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", ownerToken));
        ownerBalanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetList_DoesNotIncludeOtherUsersOrSoftDeletedAccounts()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var strangerToken = await RegisterAndLoginAsync();

        var strangerCreate = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", strangerToken);
        strangerCreate.Content = JsonContent.Create(new
        {
            accountName = "Ví của người lạ",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = 0,
        });
        await _client.SendAsync(strangerCreate);

        var ownerCreate = AuthedRequest(HttpMethod.Post, "/api/v1/financial-accounts", ownerToken);
        ownerCreate.Content = JsonContent.Create(new
        {
            accountName = "Ví sẽ bị xóa",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = 0,
        });
        var ownerCreateResponse = await _client.SendAsync(ownerCreate);
        var ownerAccountId = (await ownerCreateResponse.Content.ReadFromJsonAsync<FinancialAccountBody>(JsonOptions))!.Data.Id;

        await _client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/api/v1/financial-accounts/{ownerAccountId}", ownerToken));

        var listResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/financial-accounts", ownerToken));
        var listBody = await listResponse.Content.ReadFromJsonAsync<FinancialAccountListBody>(JsonOptions);

        listBody!.Data.Should().BeEmpty();
    }

    private record FinancialAccountListBody(bool Success, List<FinancialAccountData> Data);
}
