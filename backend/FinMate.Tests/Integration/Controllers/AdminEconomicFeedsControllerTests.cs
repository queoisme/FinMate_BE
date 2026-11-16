using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Nút "Chạy ngay" của số liệu kinh tế: chỉ Admin, và mặc định (chưa đặt địa chỉ nguồn) không gọi mạng.</summary>
[Collection("Integration")]
public class AdminEconomicFeedsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AdminEconomicFeedsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record RefreshBody(bool Success, RefreshData Data);
    private record RefreshData(string Outcome);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Economic Feed Test"));
        return await LoginAsync(email, "Password123!");
    }

    private static HttpRequestMessage Refresh(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/economic-feeds/refresh");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task AnOrdinaryUserCannotRunTheRefresh()
        => (await _client.SendAsync(Refresh(await RegisterAndLoginAsync()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task AnonymousRequestIsRejected()
        => (await _client.SendAsync(Refresh(null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task AdminGetsDisabledWhileNoSourceAddressIsConfigured()
    {
        var token = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        var response = await _client.SendAsync(Refresh(token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<RefreshBody>(JsonOptions))!.Data.Outcome.Should().Be("Disabled");
    }
}
