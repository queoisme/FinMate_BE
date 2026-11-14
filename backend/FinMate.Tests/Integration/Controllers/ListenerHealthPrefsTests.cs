using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>D18: công tắc <c>listenerHealthAlertsEnabled</c> mặc định bật, đổi được qua <c>PATCH /users/me/notification-prefs</c>, và app cũ không gửi trường này thì giữ nguyên.</summary>
[Collection("Integration")]
public class ListenerHealthPrefsTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public ListenerHealthPrefsTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ProfileEnvelope(bool Success, ProfileBody Data);
    private record ProfileBody(bool PushEnabled, bool BudgetAlertsEnabled, bool ListenerHealthAlertsEnabled);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Prefs Test User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private async Task<ProfileBody> ProfileAsync(string token)
        => (await (await SendAsync(HttpMethod.Get, "/api/v1/users/me", token)).Content.ReadFromJsonAsync<ProfileEnvelope>(JsonOptions))!.Data;

    [Fact]
    public async Task TheSwitch_DefaultsOn_CanBeTurnedOff_AndAnOldClientLeavesItAlone()
    {
        var token = await RegisterAndLoginAsync();
        (await ProfileAsync(token)).ListenerHealthAlertsEnabled.Should().BeTrue();

        (await SendAsync(HttpMethod.Patch, "/api/v1/users/me/notification-prefs", token,
            new { pushEnabled = true, budgetAlertsEnabled = true, listenerHealthAlertsEnabled = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ProfileAsync(token)).ListenerHealthAlertsEnabled.Should().BeFalse();

        (await SendAsync(HttpMethod.Patch, "/api/v1/users/me/notification-prefs", token,
            new { pushEnabled = true, budgetAlertsEnabled = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var profile = await ProfileAsync(token);
        profile.ListenerHealthAlertsEnabled.Should().BeFalse();
        profile.BudgetAlertsEnabled.Should().BeFalse();
    }
}
