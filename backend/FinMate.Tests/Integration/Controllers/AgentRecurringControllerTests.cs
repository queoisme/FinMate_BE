using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Khoản định kỳ của agent trên Postgres thật (kiểm luôn migration): xác thực, xác nhận, liệt kê, xóa, và cô lập giữa các người dùng.</summary>
[Collection("Integration")]
public class AgentRecurringControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Base = "/api/v1/agent/recurring";
    private readonly HttpClient _client;

    public AgentRecurringControllerTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ItemData(Guid Id, string Name, long AmountVnd, string Kind, int DayOfMonth);
    private record SuggestionData(string Name, long AmountVnd, string Kind, int DayOfMonth, int Months);
    private record ListData(List<ItemData> Items, List<SuggestionData> Suggestions);
    private record ListBody(bool Success, ListData Data);
    private record ItemBody(bool Success, ItemData Data);
    private record ErrorData(string Code);
    private record ErrorBody(bool Success, ErrorData Error);

    private async Task<string> TokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Recurring Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage Req(HttpMethod method, string url, string? token, object? body = null)
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

    [Fact]
    public async Task Anonymous_Is401_OnEveryRoute()
    {
        foreach (var (method, url) in new[] { (HttpMethod.Get, Base), (HttpMethod.Post, Base), (HttpMethod.Delete, $"{Base}/{Guid.NewGuid()}") })
        {
            (await _client.SendAsync(Req(method, url, null, method == HttpMethod.Post ? new { } : null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task ConfirmingListsAndDeleting_RoundTripsOnARealDatabase()
    {
        var token = await TokenAsync();

        var created = await _client.SendAsync(Req(HttpMethod.Post, Base, token, new { name = "Tiền nhà", amountVnd = 3_000_000, kind = "obligation", dayOfMonth = 12 }));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await created.Content.ReadFromJsonAsync<ItemBody>(JsonOptions))!.Data;

        var listed = (await (await _client.SendAsync(Req(HttpMethod.Get, Base, token))).Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data;
        listed.Items.Should().ContainSingle().Which.Should().Be(item);
        listed.Items[0].Should().Match<ItemData>(i => i.AmountVnd == 3_000_000 && i.Kind == "obligation" && i.DayOfMonth == 12);
        listed.Suggestions.Should().BeEmpty(); // người dùng mới chưa có lịch sử giao dịch để đề xuất

        (await _client.SendAsync(Req(HttpMethod.Delete, $"{Base}/{item.Id}", token))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.SendAsync(Req(HttpMethod.Get, Base, token))).Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task OneUserNeverSeesOrDeletesAnothersItem()
    {
        var mine = await TokenAsync();
        var theirs = await TokenAsync();
        var created = await _client.SendAsync(Req(HttpMethod.Post, Base, mine, new { name = "Của tôi", amountVnd = 1_000_000, kind = "inflow", dayOfMonth = 5 }));
        var id = (await created.Content.ReadFromJsonAsync<ItemBody>(JsonOptions))!.Data.Id;

        (await (await _client.SendAsync(Req(HttpMethod.Get, Base, theirs))).Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data.Items.Should().BeEmpty();
        (await _client.SendAsync(Req(HttpMethod.Delete, $"{Base}/{id}", theirs))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await _client.SendAsync(Req(HttpMethod.Get, Base, mine))).Content.ReadFromJsonAsync<ListBody>(JsonOptions))!.Data.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("", 1000L, "obligation", 1, "invalid_recurring_name")]
    [InlineData("x", 0L, "obligation", 1, "invalid_recurring_amount")]
    [InlineData("x", 1000L, "bogus", 1, "invalid_recurring_kind")]
    [InlineData("x", 1000L, "obligation", 40, "invalid_recurring_day")]
    public async Task InvalidInput_Is422_WithAFixedCode(string name, long amount, string kind, int day, string code)
    {
        var token = await TokenAsync();

        var response = await _client.SendAsync(Req(HttpMethod.Post, Base, token, new { name, amountVnd = amount, kind, dayOfMonth = day }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
    }
}
