using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class SecurityHeadersTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public SecurityHeadersTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthBody(bool Success, AuthData Data);
    private record AuthData(string AccessToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.AdminEmail, password = AuthApiFactory.AdminPassword });
        return (await response.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage AdminRequest(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/admin/users")] // 401 không token: header phải có cả trên response lỗi
    public async Task EveryResponseCarriesTheBasicSecurityHeaders(string url)
    {
        var response = await _client.GetAsync(url);

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
    }

    [Fact]
    public async Task AdminResponsesAreNotCached()
    {
        var response = await _client.SendAsync(AdminRequest("/api/v1/admin/users?limit=1", await AdminTokenAsync()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task AMalformedListCursorIsRejectedOnAdminLists()
    {
        var response = await _client.SendAsync(AdminRequest("/api/v1/admin/users?cursor=@@@", await AdminTokenAsync()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("VALIDATION_ERROR");
    }
}
