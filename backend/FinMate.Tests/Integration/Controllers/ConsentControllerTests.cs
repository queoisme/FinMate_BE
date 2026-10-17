using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Auth.Commands;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Đồng ý điều khoản chung và đồng ý gửi tên món cho AI: theo từng người dùng, chỉ đổi qua POST/DELETE /users/me/consents.</summary>
[Collection("Integration")]
public class ConsentControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ConsentControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ProfileData(Guid Id, bool TermsAccepted, bool AiConsentGranted, bool TrainingConsentGranted);
    private record ProfileBody(bool Success, ProfileData Data);
    private record ErrorBody(ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Consent User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private async Task<ProfileData> ProfileAsync(string token)
    {
        var response = await SendAsync(token, HttpMethod.Get, "/api/v1/users/me");
        return (await response.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data;
    }

    private async Task<HttpResponseMessage> AcceptAsync(string token, string kind, int version) =>
        await SendAsync(token, HttpMethod.Post, "/api/v1/users/me/consents", new { kind, version });

    [Fact]
    public async Task Consents_FullFlow_OverHttp()
    {
        var token = await RegisterAndLoginAsync();

        var fresh = await ProfileAsync(token);
        fresh.TermsAccepted.Should().BeFalse();
        fresh.AiConsentGranted.Should().BeFalse();

        (await AcceptAsync(token, "terms", ConsentVersions.Terms)).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterTerms = await ProfileAsync(token);
        afterTerms.TermsAccepted.Should().BeTrue("phải được lưu, không chỉ trả lại trong response");
        afterTerms.AiConsentGranted.Should().BeTrue("điều khoản chung đã nêu việc gửi tên món cho AI nên một lần đồng ý là đủ");
        afterTerms.TrainingConsentGranted.Should().BeFalse("đóng góp dữ liệu huấn luyện luôn là lựa chọn riêng");

        // Đổi tên không được đụng tới đồng ý.
        (await SendAsync(token, HttpMethod.Patch, "/api/v1/users/me", new { displayName = "Renamed" })).EnsureSuccessStatusCode();
        (await ProfileAsync(token)).AiConsentGranted.Should().BeTrue();

        (await SendAsync(token, HttpMethod.Delete, "/api/v1/users/me/consents/ai")).StatusCode.Should().Be(HttpStatusCode.OK);
        var withdrawn = await ProfileAsync(token);
        withdrawn.AiConsentGranted.Should().BeFalse();
        withdrawn.TermsAccepted.Should().BeTrue();

        // Bật lại bằng công tắc trong Hồ sơ
        (await AcceptAsync(token, "ai", ConsentVersions.Ai)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProfileAsync(token)).AiConsentGranted.Should().BeTrue();
    }

    [Fact]
    public async Task TermsOfAnOlderVersion_AreRefused_SoAnOldAppCannotAgreeToTheNewText()
    {
        var token = await RegisterAndLoginAsync();

        var response = await AcceptAsync(token, "terms", ConsentVersions.Terms - 1);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var profile = await ProfileAsync(token);
        profile.TermsAccepted.Should().BeFalse();
        profile.AiConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task TrainingConsent_IsSeparateFromAiConsent_AndWithdrawable()
    {
        var token = await RegisterAndLoginAsync();
        (await AcceptAsync(token, "ai", ConsentVersions.Ai)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProfileAsync(token)).TrainingConsentGranted.Should().BeFalse("đồng ý AI không kéo theo đồng ý thu dữ liệu huấn luyện");

        (await AcceptAsync(token, "training", ConsentVersions.Training)).StatusCode.Should().Be(HttpStatusCode.OK);
        var both = await ProfileAsync(token);
        both.TrainingConsentGranted.Should().BeTrue();
        both.AiConsentGranted.Should().BeTrue();

        (await SendAsync(token, HttpMethod.Delete, "/api/v1/users/me/consents/training")).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await ProfileAsync(token);
        after.TrainingConsentGranted.Should().BeFalse();
        after.AiConsentGranted.Should().BeTrue();
    }

    [Fact]
    public async Task WithdrawingTheTerms_IsRefused()
    {
        var token = await RegisterAndLoginAsync();
        await AcceptAsync(token, "terms", ConsentVersions.Terms);

        var response = await SendAsync(token, HttpMethod.Delete, "/api/v1/users/me/consents/terms");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProfileAsync(token)).TermsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptingAStaleVersion_Returns409WithTheOutdatedCode()
    {
        var token = await RegisterAndLoginAsync();

        var response = await AcceptAsync(token, "ai", ConsentVersions.Ai - 1);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("AUTH_CONSENT_VERSION_OUTDATED");
        (await ProfileAsync(token)).AiConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task PatchProfile_CanNoLongerGrantConsent()
    {
        var token = await RegisterAndLoginAsync();

        (await SendAsync(token, HttpMethod.Patch, "/api/v1/users/me", new { displayName = "X", aiConsentGranted = true, shareReceiptItemsWithAi = true }))
            .EnsureSuccessStatusCode();

        (await ProfileAsync(token)).AiConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task Consent_OnlyAffectsTheCaller()
    {
        var alice = await RegisterAndLoginAsync();
        var bob = await RegisterAndLoginAsync();

        await AcceptAsync(alice, "ai", ConsentVersions.Ai);

        (await ProfileAsync(alice)).AiConsentGranted.Should().BeTrue();
        (await ProfileAsync(bob)).AiConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task Consent_RequiresLogin()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/me/consents", new { kind = "ai", version = ConsentVersions.Ai }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
