using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Lỗi do framework tự trả (403 policy, 404/405 sai route) vẫn phải có envelope JSON như mọi lỗi khác.</summary>
[Collection("Integration")]
public class StatusCodeEnvelopeTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public StatusCodeEnvelopeTests(AuthApiFactory factory) => _client = factory.CreateClient();

    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code, string Message);
    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);

    [Fact]
    public async Task FrameworkErrors_ReturnJsonEnvelope()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Envelope Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;

        foreach (var (method, url, status, code) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/ai-config/documents", HttpStatusCode.Forbidden, "FORBIDDEN"),
                     (HttpMethod.Get, "/api/v1/khong-ton-tai", HttpStatusCode.NotFound, "NOT_FOUND"),
                     (HttpMethod.Get, "/api/v1/auth/login", HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED"),
                 })
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(status, url);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
            body!.Success.Should().BeFalse();
            body.Error.Code.Should().Be(code, url);
        }
    }
}
