using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary><c>POST /chat/analyze</c>: cần đăng nhập, trả nguyên kết quả AI, và KHÔNG tạo giao dịch nào.</summary>
[Collection("Integration")]
public class ChatAnalyzeControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public ChatAnalyzeControllerTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private async Task<string> LoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Analyze User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> PostAsync(string url, string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task RequiresAuthentication()
    {
        (await PostAsync("/api/v1/chat/analyze", null, new { text = "ăn phở 45k" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReturnsTheAiParseAndCreatesNoTransaction()
    {
        const string text = "analyze-only: ăn phở 40k và cà phê";
        FakeAIServiceClient.SetChat(text, new ChatParseResponse(
            "add_expense", null, null, null, 4,
            [new ChatParseEntry("expense", 40_000, "ăn phở", null, "food", null, false, false, null, "rule")],
            TotalAmount: null, TotalMismatch: false, Skipped: ["cà phê"]));
        var token = await LoginAsync();

        var response = await PostAsync("/api/v1/chat/analyze", token, new { text });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("intent").GetString().Should().Be("add_expense");
        data.GetProperty("skipped")[0].GetString().Should().Be("cà phê");
        data.GetProperty("entries")[0].GetProperty("categorySource").GetString().Should().Be("rule");

        var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/transactions?limit=20");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listed = JsonDocument.Parse(await (await _client.SendAsync(list)).Content.ReadAsStringAsync());
        listed.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsEmptyText(string text)
    {
        var response = await PostAsync("/api/v1/chat/analyze", await LoginAsync(), new { text });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
