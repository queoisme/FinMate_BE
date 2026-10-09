using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Cờ đồng ý chia sẻ tên món trên hoá đơn với AI: theo từng người dùng, chỉ đổi qua PATCH /users/me.</summary>
[Collection("Integration")]
public class ShareReceiptItemsConsentControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ShareReceiptItemsConsentControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ProfileData(Guid Id, bool ShareReceiptItemsWithAi);
    private record ProfileBody(bool Success, ProfileData Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Consent User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<bool> GetFlagAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.ShareReceiptItemsWithAi;
    }

    private async Task<bool> PatchAsync(string token, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/users/me")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.ShareReceiptItemsWithAi;
    }

    [Fact]
    public async Task ConsentFlag_FullFlow_OverHttp()
    {
        var token = await RegisterAndLoginAsync();

        (await GetFlagAsync(token)).Should().BeFalse();

        (await PatchAsync(token, new UpdateProfileRequest("Consent User", null, true))).Should().BeTrue();
        (await GetFlagAsync(token)).Should().BeTrue("phải được lưu, không chỉ trả lại trong response");

        // Không gửi trường -> giữ nguyên.
        (await PatchAsync(token, new { displayName = "Renamed" })).Should().BeTrue();
        (await GetFlagAsync(token)).Should().BeTrue();

        (await PatchAsync(token, new UpdateProfileRequest("Renamed", null, false))).Should().BeFalse();
        (await GetFlagAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task Patch_OnlyAffectsTheCaller()
    {
        var alice = await RegisterAndLoginAsync();
        var bob = await RegisterAndLoginAsync();

        await PatchAsync(alice, new UpdateProfileRequest("Consent User", null, true));

        (await GetFlagAsync(alice)).Should().BeTrue();
        (await GetFlagAsync(bob)).Should().BeFalse();
    }
}
