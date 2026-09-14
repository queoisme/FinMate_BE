using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Cho tới Phase 14, hai policy rate limit được ĐỊNH NGHĨA nhưng không gắn vào endpoint nào,
/// nên <c>UseRateLimiter()</c> chạy mà không chặn gì. Test này tồn tại để tình trạng đó không
/// quay lại một cách im lặng.
///
/// Hạn mức mặc định phân vùng theo user id nên test có ngăn riêng — không ảnh hưởng 449 test
/// còn lại dùng chung TestServer.
/// </summary>
[Collection("Integration")]
public class RateLimitingControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Khớp mặc định ở <c>RateLimitingMiddleware</c>; test không nới biến này.</summary>
    private const int DefaultPermitPerMinute = 120;

    private readonly HttpClient _client;

    public RateLimitingControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Rate Limit User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> GetProfileAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task OneUserFloodingIsEventuallyRejected()
    {
        var token = await RegisterAndLoginAsync();

        HttpStatusCode? rejected = null;
        for (var i = 0; i < DefaultPermitPerMinute + 5; i++)
        {
            var response = await GetProfileAsync(token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response.StatusCode;
                break;
            }
        }

        rejected.Should().Be(HttpStatusCode.TooManyRequests,
            "hạn mức mặc định phải THỰC SỰ được gắn vào endpoint, không chỉ được định nghĩa");
    }

    [Fact]
    public async Task OneUserHittingTheLimitDoesNotAffectAnother()
    {
        // Đây là lý do phân vùng theo user id thay vì IP: cả một trường học sau cùng một NAT
        // không được dùng chung hạn mức, và một hàng đợi offline đồng bộ dồn dập của người này
        // không được làm người khác bị chặn.
        var noisy = await RegisterAndLoginAsync();
        var quiet = await RegisterAndLoginAsync();

        for (var i = 0; i < DefaultPermitPerMinute + 5; i++)
        {
            var response = await GetProfileAsync(noisy);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        (await GetProfileAsync(quiet)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
