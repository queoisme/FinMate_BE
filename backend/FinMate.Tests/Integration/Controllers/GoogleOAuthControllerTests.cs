using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Auth.GoogleOAuth;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Luồng đăng nhập Google qua trình duyệt nhúng. Khác đường native ở chỗ Google chỉ nói chuyện
/// với backend, nên app không cần đăng ký package name + SHA-1.
/// </summary>
[Collection("Integration")]
public class GoogleOAuthControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string RedirectUri = "http://localhost:8080/api/v1/auth/google/callback";

    private readonly AuthApiFactory _factory;

    public GoogleOAuthControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    /// <summary>Trả đúng id_token dựng sẵn, thay cho vòng gọi thật sang Google.</summary>
    private sealed class StubExchanger : IGoogleCodeExchanger
    {
        public string? IdToken { get; set; }

        public Task<string?> ExchangeForIdTokenAsync(string code, CancellationToken ct = default)
            => Task.FromResult(IdToken);
    }

    private readonly StubExchanger _exchanger = new();

    /// <summary>
    /// Client KHÔNG tự đi theo redirect: cả bài test nằm ở chỗ kiểm đúng cái Location trả về.
    /// </summary>
    private HttpClient Client(bool enabled = true, string? deepLink = null)
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<GoogleOAuthOptions>();
            services.AddSingleton(new GoogleOAuthOptions(
                enabled, "test-client-id.apps.googleusercontent.com", RedirectUri, deepLink));
            services.RemoveAll<IGoogleCodeExchanger>();
            services.AddSingleton<IGoogleCodeExchanger>(_exchanger);
        }));

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    private void ArrangeGoogleUser(string email) =>
        _exchanger.IdToken = FakeGoogleTokenVerifier.TokenFor(
            new GoogleUserInfo($"sub-{Guid.NewGuid():N}", email, true, "Người Dùng Google"));

    private static string NewEmail() => $"{Guid.NewGuid():N}@finmate.local";

    /// <summary>Đi hết /start rồi /callback, trả về phản hồi của callback.</summary>
    private async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email)
    {
        ArrangeGoogleUser(email);

        var start = await client.GetAsync("/api/v1/auth/google/start");
        var state = System.Web.HttpUtility
            .ParseQueryString(new Uri(start.Headers.Location!.ToString()).Query)["state"];

        return await client.GetAsync($"/api/v1/auth/google/callback?code=google-code&state={state}");
    }

    // ------------------------------------------------------------------ start

    [Fact]
    public async Task StartSendsTheBrowserToGoogleWithAState()
    {
        var response = await Client().GetAsync("/api/v1/auth/google/start");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://accounts.google.com/o/oauth2/v2/auth");

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(location).Query);
        query["state"].Should().NotBeNullOrWhiteSpace();
        query["redirect_uri"].Should().Be(RedirectUri);
        query["response_type"].Should().Be("code");
    }

    // --------------------------------------------------------------- callback

    [Fact]
    public async Task AForgedCallbackIsRefused()
    {
        // Không có state hợp lệ thì kẻ tấn công ghép được tài khoản Google của chúng vào
        // phiên của nạn nhân.
        var client = Client();
        ArrangeGoogleUser(NewEmail());

        var response = await client.GetAsync(
            "/api/v1/auth/google/callback?code=google-code&state=tu-bia-ra");

        (await response.Content.ReadAsStringAsync()).Should().Contain("không hợp lệ");
    }

    [Fact]
    public async Task AStateCannotBeReplayed()
    {
        var client = Client();
        var email = NewEmail();

        // Lấy state thật, dùng nó một lần qua callback, rồi thử lại đúng state đó.
        ArrangeGoogleUser(email);
        var start = await client.GetAsync("/api/v1/auth/google/start");
        var state = System.Web.HttpUtility
            .ParseQueryString(new Uri(start.Headers.Location!.ToString()).Query)["state"];

        var first = await client.GetAsync($"/api/v1/auth/google/callback?code=c1&state={state}");
        first.StatusCode.Should().Be(HttpStatusCode.OK, "lần đầu phải thành công");

        var replay = await client.GetAsync($"/api/v1/auth/google/callback?code=c2&state={state}");

        (await replay.Content.ReadAsStringAsync()).Should().Contain("không hợp lệ");
    }

    [Fact]
    public async Task CancellingAtGooglesScreenSaysSoPlainly()
    {
        var response = await Client().GetAsync("/api/v1/auth/google/callback?error=access_denied");

        (await response.Content.ReadAsStringAsync()).Should().Contain("huỷ");
    }

    [Fact]
    public async Task TheRedirectCarriesACodeAndNeverATokenS()
    {
        // Điểm cốt tử. URL rơi vào lịch sử trình duyệt, và trên Android một app khác đăng ký
        // trùng scheme sẽ nhận được cùng đường link — refresh token sống 30 ngày mà lọt kiểu
        // đó là mất tài khoản.
        var client = Client(deepLink: "finmate://auth");

        var response = await SignInAsync(client, NewEmail());

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();

        // ASP.NET chuẩn hoá URI nên ra "finmate://auth/?code=…" — có thêm dấu gạch chéo so
        // với chuỗi cấu hình. Vô hại với intent filter của Android (khớp theo scheme + host),
        // nhưng ghi lại ở đây để phía Flutter không bất ngờ.
        location.Should().StartWith("finmate://auth").And.Contain("?code=");

        location.Should().NotContain("accessToken").And.NotContain("refreshToken");
        location.Should().NotContain("eyJ", "JWT luôn bắt đầu bằng chuỗi này");
    }

    [Fact]
    public async Task WithoutADeepLinkTheCodeIsShownForManualTesting()
    {
        // Chế độ thử: kiểm chứng được toàn bộ luồng bằng trình duyệt máy tính, không cần app.
        var response = await SignInAsync(Client(deepLink: null), NewEmail());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Mã bàn giao");
    }

    // --------------------------------------------------------------- exchange

    [Fact]
    public async Task TheCodeBuysTokensExactlyOnce()
    {
        var client = Client(deepLink: "finmate://auth");
        var callback = await SignInAsync(client, NewEmail());
        var code = System.Web.HttpUtility
            .ParseQueryString(callback.Headers.Location!.ToString().Split('?')[1])["code"];

        var first = await client.PostAsJsonAsync(
            "/api/v1/auth/google/exchange", new GoogleHandoffRequest(code!), JsonOptions);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = (await first.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data;
        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();

        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/google/exchange", new GoogleHandoffRequest(code!), JsonOptions);
        second.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheIssuedTokenBelongsToTheGoogleAccount()
    {
        var client = Client(deepLink: "finmate://auth");
        var email = NewEmail();
        var callback = await SignInAsync(client, email);
        var code = System.Web.HttpUtility
            .ParseQueryString(callback.Headers.Location!.ToString().Split('?')[1])["code"];

        var exchange = await client.PostAsJsonAsync(
            "/api/v1/auth/google/exchange", new GoogleHandoffRequest(code!), JsonOptions);
        var tokens = (await exchange.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data;

        var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", tokens.AccessToken);
        var profile = await client.SendAsync(me);

        (await profile.Content.ReadAsStringAsync()).Should().Contain(email);
    }

    [Fact]
    public async Task AnUnknownCodeBuysNothing()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/auth/google/exchange", new GoogleHandoffRequest("khong-co-that"), JsonOptions);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------- chưa cấu hình

    [Fact]
    public async Task WithoutAClientSecretTheFlowSaysSoInsteadOfCrashing()
    {
        var response = await Client(enabled: false).GetAsync("/api/v1/auth/google/start");

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("AUTH_GOOGLE_OAUTH_NOT_CONFIGURED");
    }
}
