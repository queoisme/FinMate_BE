using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// <c>POST /transactions/chat</c> trên Postgres thật, với AI Service giả: AI chỉ hiểu câu, backend quyết định ghi hay hỏi
/// và tự tính tổng. Mỗi test dùng một câu chat riêng vì kịch bản của AI giả khóa theo đúng câu gửi lên.
/// </summary>
[Collection("Integration")]
public class ChatControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ChatControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Chat Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<Guid> CreateCashAccountAsync(string token, long balance = 1_000_000)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/financial-accounts", token, new
        {
            accountName = "Ví tiền mặt",
            accountType = "Cash",
            providerConfigId = (Guid?)null,
            initialBalanceCents = balance,
        }));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<long> BalanceOfAsync(string token, Guid accountId)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", token));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("balanceCents").GetInt64();
    }

    private async Task<JsonElement> ChatAsync(string token, string text)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions/chat", token, new { text }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static ChatParseResponse Expense(string text, long amount, bool auto, string? category = "food")
        => new("add_expense", null, new ChatParseEntry("expense", amount, text, null, category, null, auto, false), null, 5);

    [Fact]
    public async Task ChatRequiresAuthentication()
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions/chat", null, new { text = "đi ăn 200k" }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AConfidentExpenseIsSavedChargesTheAccountAndCanBeUndone()
    {
        const string text = "chat-test: đi ăn 200k";
        FakeAIServiceClient.SetChat(text, Expense(text, 200_000, auto: true));
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token, 1_000_000);

        var data = await ChatAsync(token, text);

        data.GetProperty("action").GetString().Should().Be(ChatActions.Saved);
        var transaction = data.GetProperty("transaction");
        transaction.GetProperty("amountCents").GetInt64().Should().Be(200_000);
        transaction.GetProperty("status").GetString().Should().Be("Confirmed");
        transaction.GetProperty("categoryName").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await BalanceOfAsync(token, accountId)).Should().Be(800_000);

        // Hoàn tác = DELETE có sẵn: trả lại đúng số dư.
        var id = transaction.GetProperty("id").GetGuid();
        var undo = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/transactions/{id}", token));
        undo.IsSuccessStatusCode.Should().BeTrue();
        (await BalanceOfAsync(token, accountId)).Should().Be(1_000_000);
    }

    [Fact]
    public async Task AnExpenseTheAiIsNotSureAboutCreatesNothing()
    {
        const string text = "chat-test: mua áo 3 triệu";
        FakeAIServiceClient.SetChat(text, Expense(text, 3_000_000, auto: false));
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token, 5_000_000);

        var data = await ChatAsync(token, text);

        data.GetProperty("action").GetString().Should().Be(ChatActions.Confirm);
        data.GetProperty("transaction").ValueKind.Should().Be(JsonValueKind.Null);
        var draft = data.GetProperty("draft");
        draft.GetProperty("amountCents").GetInt64().Should().Be(3_000_000);
        draft.GetProperty("suggestedFinancialAccountId").GetGuid().Should().Be(accountId);
        (await BalanceOfAsync(token, accountId)).Should().Be(5_000_000);
    }

    [Fact]
    public async Task WithoutAnyAccountEvenAConfidentExpenseOnlyBecomesADraft()
    {
        const string text = "chat-test: cafe 35k";
        FakeAIServiceClient.SetChat(text, Expense(text, 35_000, auto: true));
        var token = await RegisterAndLoginAsync();

        var data = await ChatAsync(token, text);

        data.GetProperty("action").GetString().Should().Be(ChatActions.Confirm);
        data.GetProperty("draft").GetProperty("missingFields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(ChatMissingFields.Account);
    }

    [Fact]
    public async Task TotalsComeFromTheUsersOwnTransactions()
    {
        const string spend = "chat-test: ăn tối 150k";
        const string ask = "chat-test: tháng này tôi tiêu bao nhiêu";
        FakeAIServiceClient.SetChat(spend, Expense(spend, 150_000, auto: true));
        var (monthStart, nextMonth) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        FakeAIServiceClient.SetChat(ask, new ChatParseResponse(
            "ask_spending", null, null,
            new ChatParseQuery("expense", "tháng này", monthStart.ToOffset(VietnamTime.Offset), nextMonth.ToOffset(VietnamTime.Offset), null, null), 5));
        var token = await RegisterAndLoginAsync();
        await CreateCashAccountAsync(token);
        await ChatAsync(token, spend);
        await ChatAsync(token, spend);

        var data = await ChatAsync(token, ask);

        data.GetProperty("action").GetString().Should().Be(ChatActions.Summary);
        var summary = data.GetProperty("summary");
        summary.GetProperty("totalCents").GetInt64().Should().Be(300_000);
        summary.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("categorySlug").GetString().Should().Be("food");
    }

    [Fact]
    public async Task SomeoneElsesTransactionsNeverShowUpInMyTotals()
    {
        const string spend = "chat-test: grab 80k";
        const string ask = "chat-test: tháng này chi bao nhiêu (người khác)";
        FakeAIServiceClient.SetChat(spend, Expense(spend, 80_000, auto: true));
        var (monthStart, nextMonth) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        FakeAIServiceClient.SetChat(ask, new ChatParseResponse(
            "ask_spending", null, null,
            new ChatParseQuery("expense", "tháng này", monthStart.ToOffset(VietnamTime.Offset), nextMonth.ToOffset(VietnamTime.Offset), null, null), 5));
        var other = await RegisterAndLoginAsync();
        await CreateCashAccountAsync(other);
        await ChatAsync(other, spend);
        var me = await RegisterAndLoginAsync();

        var data = await ChatAsync(me, ask);

        data.GetProperty("summary").GetProperty("totalCents").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task EmptyTextIsRejected()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/v1/transactions/chat", token, new { text = "  " }));

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }
}
