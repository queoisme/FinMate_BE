using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Dạy lại bộ nhớ cửa hàng qua tầng HTTP thật: lưu hoặc sửa danh mục của một khoản chi từ hóa đơn thì backend nhờ AI Service nhớ.
/// AI Service giả ghi lại các lần được nhờ; mỗi test dùng tên cửa hàng riêng để không lẫn nhau.
/// </summary>
[Collection("Integration")]
public class ReceiptLearningControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public ReceiptLearningControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Learn Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private async Task<Guid> SystemCategoryAsync(string token, string slug)
    {
        var data = await DataOf(await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/categories", token)));
        return data.EnumerateArray().First(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CashAccountAsync(string token)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/financial-accounts", token, new
        {
            accountName = "Ví tiền mặt", accountType = "Cash", providerConfigId = (Guid?)null, initialBalanceCents = 1_000_000,
        }));
        return (await DataOf(response)).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> CreateAsync(string token, Guid account, Guid category, string merchant, string source)
        => await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = account, categoryId = category, amountCents = 16_000, transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = merchant, description = (string?)null, source,
        }));

    [Fact]
    public async Task SavingAReceiptExpenseTeachesTheAiTheChosenCategory()
    {
        const string merchant = "Learn Cafe Create Test";
        var token = await RegisterAndLoginAsync();
        var account = await CashAccountAsync(token);
        var entertainment = await SystemCategoryAsync(token, "entertainment");

        var response = await CreateAsync(token, account, entertainment, merchant, "Receipt");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var learned = FakeAIServiceClient.Learned(merchant).Should().ContainSingle().Subject;
        learned.CategorySlug.Should().Be("entertainment");
        learned.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SavingTheSameReceiptTwiceThroughAReplayTeachesOnlyOnce()
    {
        const string merchant = "Learn Cafe Replay Test";
        var token = await RegisterAndLoginAsync();
        var account = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");
        var clientRequestId = Guid.NewGuid();
        object Body() => new
        {
            financialAccountId = account, categoryId = food, amountCents = 16_000, transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = merchant, description = (string?)null, source = "Receipt", clientRequestId,
        };

        await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions", token, Body()));
        var replay = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions", token, Body()));

        replay.StatusCode.Should().Be(HttpStatusCode.OK);  // gửi lại: không tạo mới
        FakeAIServiceClient.Learned(merchant).Should().ContainSingle();
    }

    [Fact]
    public async Task ManualEntriesTeachNothing()
    {
        const string merchant = "Learn Cafe Manual Test";
        var token = await RegisterAndLoginAsync();
        var account = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");

        await CreateAsync(token, account, food, merchant, "Manual");

        FakeAIServiceClient.Learned(merchant).Should().BeEmpty();
    }

    [Fact]
    public async Task CorrectingTheCategoryOfASavedReceiptTeachesTheNewOne()
    {
        const string merchant = "Learn Cafe Patch Test";
        var token = await RegisterAndLoginAsync();
        var account = await CashAccountAsync(token);
        var food = await SystemCategoryAsync(token, "food");
        var health = await SystemCategoryAsync(token, "health");
        var created = await DataOf(await CreateAsync(token, account, food, merchant, "Receipt"));
        FakeAIServiceClient.Learned(merchant).Single().CategorySlug.Should().Be("food");

        var response = await _client.SendAsync(Authed(HttpMethod.Patch, $"/api/v1/transactions/{created.GetProperty("id").GetGuid()}", token, new
        {
            financialAccountId = account, counterAccountId = (Guid?)null, categoryId = health, amountCents = 16_000, transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow, merchantName = merchant, description = (string?)null,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FakeAIServiceClient.Learned(merchant).Select(l => l.CategorySlug).Should().Equal("food", "health");
    }
}
