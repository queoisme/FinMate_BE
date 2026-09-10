using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class GoogleLoginControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public GoogleLoginControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    [Fact]
    public async Task GoogleLogin_InvalidToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new { idToken = FakeGoogleTokenVerifier.InvalidToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GoogleLogin_NewUser_CreatesAccountAndIssuesTokens()
    {
        var googleInfo = new GoogleUserInfo(
            Sub: $"sub-{Guid.NewGuid():N}", Email: $"{Guid.NewGuid():N}@gmail.com", EmailVerified: true, Name: "Google User");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new { idToken = FakeGoogleTokenVerifier.TokenFor(googleInfo) });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        body!.Data.AccessToken.Should().NotBeNullOrEmpty();

        // Logging in again with the same Google identity must reuse the same account, not 500/duplicate.
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new { idToken = FakeGoogleTokenVerifier.TokenFor(googleInfo) });
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GoogleLogin_SameEmailAsExistingPasswordAccount_AutoLinksAndBothLoginMethodsWork()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Password First User"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var googleInfo = new GoogleUserInfo(Sub: $"sub-{Guid.NewGuid():N}", Email: email, EmailVerified: true, Name: "Password First User");
        var googleLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new { idToken = FakeGoogleTokenVerifier.TokenFor(googleInfo) });
        googleLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Original password login must still work after auto-linking.
        var passwordLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, password));
        passwordLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GoogleLogin_UnverifiedEmail_ReturnsUnauthorized()
    {
        var googleInfo = new GoogleUserInfo(
            Sub: $"sub-{Guid.NewGuid():N}", Email: $"{Guid.NewGuid():N}@gmail.com", EmailVerified: false, Name: "Unverified");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google",
            new { idToken = FakeGoogleTokenVerifier.TokenFor(googleInfo) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
