using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class BudgetsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public BudgetsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record CategoryData(Guid Id, string Name, string Slug, bool IsSystem);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record BudgetData(Guid Id, Guid? CategoryId, string? CategorySlug, long LimitCents, string PeriodType);
    private record BudgetBody(bool Success, BudgetData Data);
    private record SummaryItem(
        Guid BudgetId, Guid? CategoryId, string? CategorySlug,
        long LimitCents, long SpentCents, long RemainingCents, int PercentUsed, bool IsOverLimit);
    private record SummaryData(
        DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
        long TotalLimitCents, long TotalSpentCents, List<SummaryItem> Items);
    private record SummaryBody(bool Success, SummaryData Data);
    private record TransactionData(Guid Id);
    private record TransactionBody(bool Success, TransactionData Data);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Budget Test User"));

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

    private async Task<SummaryData> GetSummaryAsync(string accessToken)
    {
        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/budgets", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SummaryBody>(JsonOptions);
        return body!.Data;
    }

    [Fact]
    public async Task SpendingFlow_UpdatesCategoryBudgetAndTotalBudget_AndRevertsOnDelete()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        var categoryBudget = await PostAsync("/api/v1/budgets", accessToken,
            new { categoryId = foodId, limitCents = 1_000_000L });
        categoryBudget.StatusCode.Should().Be(HttpStatusCode.Created);

        var totalBudget = await PostAsync("/api/v1/budgets", accessToken,
            new { categoryId = (Guid?)null, limitCents = 5_000_000L });
        totalBudget.StatusCode.Should().Be(HttpStatusCode.Created);

        var txResponse = await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 250_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            merchantName = "Highlands Coffee",
        });
        txResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var transactionId = (await txResponse.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Id;

        // Một lần chi tiêu phải tiêu hạn mức của cả budget category lẫn budget tổng.
        var afterSpend = await GetSummaryAsync(accessToken);
        afterSpend.Items.Single(i => i.CategorySlug == "food").SpentCents.Should().Be(250_000);
        afterSpend.Items.Single(i => i.CategoryId is null).SpentCents.Should().Be(250_000);
        afterSpend.Items.Single(i => i.CategorySlug == "food").PercentUsed.Should().Be(25);
        // Total chỉ tổng hợp budget theo category — cộng cả budget tổng sẽ tính trùng.
        afterSpend.TotalLimitCents.Should().Be(1_000_000);
        afterSpend.TotalSpentCents.Should().Be(250_000);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/transactions/{transactionId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await GetSummaryAsync(accessToken);
        afterDelete.Items.Single(i => i.CategorySlug == "food").SpentCents.Should().Be(0);
        afterDelete.Items.Single(i => i.CategoryId is null).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task ChangingTransactionCategory_MovesSpendToTheOtherBudget()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");
        var transportId = await GetSystemCategoryIdAsync(accessToken, "transport");

        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L });
        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = transportId, limitCents = 1_000_000L });

        var transactedAt = DateTimeOffset.UtcNow;
        var txResponse = await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 300_000L,
            transactionType = "Debit",
            transactedAt,
        });
        var transactionId = (await txResponse.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Id;

        var patchRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/transactions/{transactionId}", accessToken);
        patchRequest.Content = JsonContent.Create(new
        {
            financialAccountId = accountId,
            categoryId = transportId,
            amountCents = 300_000L,
            transactionType = "Debit",
            transactedAt,
        });
        var patchResponse = await _client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await GetSummaryAsync(accessToken);
        summary.Items.Single(i => i.CategorySlug == "food").SpentCents.Should().Be(0);
        summary.Items.Single(i => i.CategorySlug == "transport").SpentCents.Should().Be(300_000);
    }

    [Fact]
    public async Task CreateBudget_MidMonth_BackfillsAlreadyConfirmedSpending()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        // Tiêu trước, đặt hạn mức sau — budget mới vẫn phải thấy phần đã tiêu trong tháng.
        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 420_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });

        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L });

        var summary = await GetSummaryAsync(accessToken);
        summary.Items.Single(i => i.CategorySlug == "food").SpentCents.Should().Be(420_000);
    }

    [Fact]
    public async Task CreditTransaction_DoesNotConsumeBudget()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var incomeId = await GetSystemCategoryIdAsync(accessToken, "income");

        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = incomeId, limitCents = 1_000_000L });

        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = incomeId,
            amountCents = 15_000_000L,
            transactionType = "Credit",
            transactedAt = DateTimeOffset.UtcNow,
        });

        var summary = await GetSummaryAsync(accessToken);
        summary.Items.Single(i => i.CategorySlug == "income").SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task CreateBudget_DuplicateCategory_ReturnsConflict()
    {
        var accessToken = await RegisterAndLoginAsync();
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L });
        var duplicate = await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 2_000_000L });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await duplicate.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("BUDGET_CATEGORY_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateBudget_SecondTotalBudget_ReturnsConflict()
    {
        var accessToken = await RegisterAndLoginAsync();

        await PostAsync("/api/v1/budgets", accessToken, new { categoryId = (Guid?)null, limitCents = 5_000_000L });
        var duplicate = await PostAsync("/api/v1/budgets", accessToken, new { categoryId = (Guid?)null, limitCents = 9_000_000L });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateLimit_ChangesLimitAndRecalculatesPercent()
    {
        var accessToken = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(accessToken);
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        var created = await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L });
        var budgetId = (await created.Content.ReadFromJsonAsync<BudgetBody>(JsonOptions))!.Data.Id;

        await PostAsync("/api/v1/transactions", accessToken, new
        {
            financialAccountId = accountId,
            categoryId = foodId,
            amountCents = 500_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });

        var patchRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/budgets/{budgetId}", accessToken);
        patchRequest.Content = JsonContent.Create(new { limitCents = 2_000_000L });
        (await _client.SendAsync(patchRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await GetSummaryAsync(accessToken);
        var item = summary.Items.Single(i => i.CategorySlug == "food");
        item.LimitCents.Should().Be(2_000_000);
        item.PercentUsed.Should().Be(25);
        item.IsOverLimit.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBudget_RemovesItFromSummary()
    {
        var accessToken = await RegisterAndLoginAsync();
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        var created = await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 1_000_000L });
        var budgetId = (await created.Content.ReadFromJsonAsync<BudgetBody>(JsonOptions))!.Data.Id;

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/budgets/{budgetId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetSummaryAsync(accessToken)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateBudget_OfAnotherUser_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var foodId = await GetSystemCategoryIdAsync(ownerToken, "food");
        var created = await PostAsync("/api/v1/budgets", ownerToken, new { categoryId = foodId, limitCents = 1_000_000L });
        var budgetId = (await created.Content.ReadFromJsonAsync<BudgetBody>(JsonOptions))!.Data.Id;

        var otherToken = await RegisterAndLoginAsync();
        var patchRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/budgets/{budgetId}", otherToken);
        patchRequest.Content = JsonContent.Create(new { limitCents = 1L });

        (await _client.SendAsync(patchRequest)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateBudget_NonPositiveLimit_ReturnsBadRequest()
    {
        var accessToken = await RegisterAndLoginAsync();
        var foodId = await GetSystemCategoryIdAsync(accessToken, "food");

        var response = await PostAsync("/api/v1/budgets", accessToken, new { categoryId = foodId, limitCents = 0L });

        // Lỗi validator là 400; 422 dành cho vi phạm quy tắc nghiệp vụ (xem ExceptionHandlingMiddleware).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
