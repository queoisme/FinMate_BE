using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// "Học AI" cho trang admin (câu chưa hiểu, phản hồi sửa kết quả): chỉ admin, chỉ đọc, đi nguyên văn tới khu vực <c>learning</c> của AI Service cùng đủ tham số cắt trang, tìm kiếm và lọc
/// (client giả ghi lại lời gọi). Cũng ghim việc danh sách model Rasa chuyển tiếp <c>limit/offset</c> (trước đây bị bỏ giữa đường nên trang 2 trả lại trang 1).
/// </summary>
[Collection("Integration")]
public class AdminAiLearningControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Base = "/api/v1/admin/ai-learning";

    private sealed class RecordingClient : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            using var document = JsonDocument.Parse("""{"items":[{"id":"1","text":"câu thử"}],"total":41,"limit":20,"offset":20}""");
            return Task.FromResult<JsonElement?>(document.RootElement.Clone());
        }
    }

    private readonly RecordingClient _ai = new();
    private readonly HttpClient _client;

    public AdminAiLearningControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_ai);
        })).CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private Task<string> AdminTokenAsync() => LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

    private async Task<string> UserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Learning Test User"));
        return await LoginAsync(email, "Password123!");
    }

    private static HttpRequestMessage Get(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUser_Is403_OnEveryRoute()
    {
        var user = await UserTokenAsync();
        foreach (var url in new[] { $"{Base}/unclear", $"{Base}/feedback" })
        {
            (await _client.SendAsync(Get(url, null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.SendAsync(Get(url, user))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _ai.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unclear", "q=tr%E1%BA%A3&status=pending&reason=disagree&limit=20&offset=20")]
    [InlineData("feedback", "q=agribank&feedback_type=category_correction&processed=false&limit=50&offset=0")]
    public async Task Listing_ForwardsPagingSearchAndFiltersToTheLearningArea_AndReturnsTheBodyAsIs(string path, string query)
    {
        var admin = await AdminTokenAsync();
        _ai.Calls.Clear();

        var response = await _client.SendAsync(Get($"{Base}/{path}?{query}", admin));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _ai.Calls.Single();
        call.Area.Should().Be(AiRulesRequest.LearningArea);
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be(path);
        call.ActorId.Should().BeNull();
        call.Query.Should().BeEquivalentTo(query.Split('&').Select(pair => pair.Split('=')).ToDictionary(
            pair => pair[0], pair => (string?)Uri.UnescapeDataString(pair[1])));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        body.GetProperty("total").GetInt32().Should().Be(41);
        body.GetProperty("offset").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task RasaModels_ForwardsLimitAndOffset()
    {
        var admin = await AdminTokenAsync();
        _ai.Calls.Clear();

        (await _client.SendAsync(Get("/api/v1/admin/rasa/models?limit=5&offset=10", admin))).StatusCode.Should().Be(HttpStatusCode.OK);

        _ai.Calls.Single().Should().Match<AiRulesRequest>(c => c.Area == AiRulesRequest.RasaArea && c.Path == "models"
            && c.Query != null && c.Query["limit"] == "5" && c.Query["offset"] == "10");
    }
}
