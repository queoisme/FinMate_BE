using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

public class AuthControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AuthControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);

    [Fact]
    public async Task RegisterLoginRefreshLogout_FullFlow_Succeeds()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Integration Test User"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        loginBody!.Data.AccessToken.Should().NotBeNullOrEmpty();
        var originalRefreshToken = loginBody.Data.RefreshToken;

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        meRequest.Headers.Add("Authorization", $"Bearer {loginBody.Data.AccessToken}");
        var meResponse = await _client.SendAsync(meRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(originalRefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        var rotatedRefreshToken = refreshBody!.Data.RefreshToken;
        rotatedRefreshToken.Should().NotBe(originalRefreshToken);

        // Reusing the now-rotated original refresh token must be rejected (reuse detection).
        var reuseResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(originalRefreshToken));
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // And since reuse revokes every session, the freshly-rotated token is dead too.
        var refreshAfterReuseResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(rotatedRefreshToken));
        refreshAfterReuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
