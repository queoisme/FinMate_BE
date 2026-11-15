using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class NotificationInboxControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public NotificationInboxControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record CategoryData(Guid Id, string Name, string Slug, bool IsSystem);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record NotificationItem(Guid Id, string Type, string Title, string Body, string? RelatedEntityType, Guid? RelatedEntityId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
    private record TabCounts(int Total, int Unread);
    private record NotificationListData(List<NotificationItem> Items, TabCounts All, TabCounts Movement, TabCounts AiAlert);
    private record NotificationListBody(bool Success, NotificationListData Data);
    private record NotificationItemBody(bool Success, NotificationItem Data);
    private record IntBody(bool Success, int Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Notification Inbox Test User"));

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

    private async Task<HttpResponseMessage> PostAsync(string url, string accessToken, object body)
    {
        var request = AuthedRequest(HttpMethod.Post, url, accessToken);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> GetSystemCategoryIdAsync(string accessToken, string slug)
    {
        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var body = await response.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        return body!.Data.First(c => c.IsSystem && c.Slug == slug).Id;
    }

    private async Task<Guid> CreateCashAccountAsync(string accessToken)
    {
        var response = await PostAsync("/api/v1/financial-accounts", accessToken, new
        {
            accountType = "Cash",
            accountName = "Ví tiền mặt",
            initialBalanceCents = 10_000_000L,
        });
        var body = await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions);
        return body!.Data.Id;
    }

    private async Task<NotificationListData> GetInboxAsync(string accessToken, string tab = "all")
    {
        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, $"/api/v1/me/notifications?tab={tab}", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NotificationListBody>(JsonOptions);
        return body!.Data;
    }

    [Fact]
    public async Task CreatingTransaction_RecordsBalanceChangeNotification_InMovementTab()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        var txResponse = await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 50_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = "Highlands Coffee",
        });
        txResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var movement = await GetInboxAsync(accessToken, "movement");
        movement.Items.Should().ContainSingle(i => i.Type == "BalanceChange" && i.RelatedEntityType == "Transaction");
        movement.Movement.Total.Should().Be(1);
        movement.Movement.Unread.Should().Be(1);
        movement.AiAlert.Total.Should().Be(0);

        var all = await GetInboxAsync(accessToken);
        all.All.Total.Should().Be(1);
    }

    [Fact]
    public async Task BudgetCrossingT1Threshold_RecordsBudgetThresholdNotification_InAiAlertTab()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        (await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // 75% hạn mức — vượt T1 mặc định (70%).
        var txResponse = await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 750_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = "Highlands Coffee",
        });
        txResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var aiAlert = await GetInboxAsync(accessToken, "ai_alert");
        aiAlert.Items.Should().ContainSingle(i => i.Type == "BudgetThreshold");
        aiAlert.AiAlert.Total.Should().Be(1);

        // Cùng giao dịch cũng sinh BalanceChange ở tab Biến động — không lẫn vào Cảnh báo AI.
        aiAlert.Movement.Total.Should().Be(1);
        aiAlert.All.Total.Should().Be(2);
    }

    [Fact]
    public async Task MarkRead_ThenMarkAllRead_UpdatesUnreadCounts()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 10_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });
        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 20_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });

        var before = await GetInboxAsync(accessToken);
        before.All.Unread.Should().Be(2);
        var firstId = before.Items.First().Id;

        var markReadResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Patch, $"/api/v1/me/notifications/{firstId}/read", accessToken));
        markReadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var marked = await markReadResponse.Content.ReadFromJsonAsync<NotificationItemBody>(JsonOptions);
        marked!.Data.ReadAt.Should().NotBeNull();

        var afterOne = await GetInboxAsync(accessToken);
        afterOne.All.Unread.Should().Be(1);

        var markAllResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Patch, "/api/v1/me/notifications/read-all", accessToken));
        markAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var markedAll = await markAllResponse.Content.ReadFromJsonAsync<IntBody>(JsonOptions);
        markedAll!.Data.Should().Be(1);

        var afterAll = await GetInboxAsync(accessToken);
        afterAll.All.Unread.Should().Be(0);
        afterAll.All.Total.Should().Be(2);
    }

    [Fact]
    public async Task DeleteSingle_ThenDeleteAll_RemovesNotifications()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 10_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });
        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 20_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });

        var before = await GetInboxAsync(accessToken);
        before.All.Total.Should().Be(2);
        var firstId = before.Items.First().Id;

        var deleteResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Delete, $"/api/v1/me/notifications/{firstId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterOneDelete = await GetInboxAsync(accessToken);
        afterOneDelete.All.Total.Should().Be(1);

        var deleteAllResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Delete, "/api/v1/me/notifications", accessToken));
        deleteAllResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeleteAll = await GetInboxAsync(accessToken);
        afterDeleteAll.All.Total.Should().Be(0);
    }

    [Fact]
    public async Task MarkRead_OfAnotherUsersNotification_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(ownerToken);
        var foodId = await GetSystemCategoryIdAsync(ownerToken, "food");

        await PostAsync("/api/v1/transactions", ownerToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 10_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });
        var ownerInbox = await GetInboxAsync(ownerToken);
        var notificationId = ownerInbox.Items.First().Id;

        var otherToken = await RegisterAndLoginAsync();
        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Patch, $"/api/v1/me/notifications/{notificationId}/read", otherToken));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
