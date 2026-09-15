using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Auth;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Hai lỗ hổng Phase 17 đóng lại: quên mật khẩu trước đó là mất tài khoản vĩnh viễn, và ai
/// cũng đăng ký được bằng email của người khác.
/// </summary>
[Collection("Integration")]
public class OtpAuthControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly CapturingEmailSender _email = new();

    public OtpAuthControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken, string RefreshToken);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    /// <summary>
    /// Client riêng cho từng test: thay <see cref="IEmailSender"/> để đọc được mã, và bật/tắt
    /// cổng xác minh trong phạm vi test thay vì đụng biến môi trường dùng chung — cả bộ test
    /// tích hợp nằm chung một tiến trình.
    /// </summary>
    private HttpClient Client(bool requireEmailVerification = false)
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);
            services.RemoveAll<AuthOptions>();
            services.AddSingleton(new AuthOptions(requireEmailVerification));
        })).CreateClient();

    private static string NewEmail() => $"{Guid.NewGuid():N}@finmate.local";
    private const string Password = "Password123!";
    private const string NewPassword = "NewPassword456!";

    private async Task RegisterAsync(HttpClient client, string email)
        => (await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, Password, "OTP Test User"))).EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

    // ------------------------------------------------------- xác minh email

    [Fact]
    public async Task AnUnverifiedAccountCannotLogInWhenTheGateIsOn()
    {
        var client = Client(requireEmailVerification: true);
        var email = NewEmail();
        await RegisterAsync(client, email);

        var response = await LoginAsync(client, email, Password);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("AUTH_EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task VerifyingWithTheEmailedCodeOpensTheAccount()
    {
        var client = Client(requireEmailVerification: true);
        var email = NewEmail();
        await RegisterAsync(client, email);

        await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new EmailOnlyRequest(email));
        var code = _email.CodeFor(email);
        code.Should().NotBeNull("mã phải được gửi tới đúng địa chỉ vừa đăng ký");

        var verify = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email", new VerifyEmailRequest(email, code!));
        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoginAsync(client, email, Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheGateOffKeepsTheOldBehaviour()
    {
        // 23 file test khác dựa vào đúng điều này.
        var client = Client(requireEmailVerification: false);
        var email = NewEmail();
        await RegisterAsync(client, email);

        (await LoginAsync(client, email, Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWrongVerificationCodeIsRefused()
    {
        var client = Client(requireEmailVerification: true);
        var email = NewEmail();
        await RegisterAsync(client, email);
        await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new EmailOnlyRequest(email));

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email", new VerifyEmailRequest(email, "000000"));

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("AUTH_OTP_INVALID");
    }

    // ------------------------------------------------------- quên mật khẩu

    [Fact]
    public async Task ResettingWithTheEmailedCodeChangesThePassword()
    {
        var client = Client();
        var email = NewEmail();
        await RegisterAsync(client, email);

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailOnlyRequest(email));
        var code = _email.CodeFor(email);
        code.Should().NotBeNull();

        var reset = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new ResetPasswordRequest(email, code!, NewPassword));
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoginAsync(client, email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, email, Password)).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResettingRevokesEveryOpenSession()
    {
        // Lý do người ta đặt lại mật khẩu thường là "tôi nghĩ có người vào được tài khoản" —
        // để phiên cũ sống tiếp là không giải quyết đúng điều đó.
        var client = Client();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var login = await LoginAsync(client, email, Password);
        var session = (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data;

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailOnlyRequest(email));
        await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(email, _email.CodeFor(email)!, NewPassword));

        var refresh = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshRequest(session.RefreshToken));

        refresh.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPasswordLooksIdenticalForAnAddressThatDoesNotExist()
    {
        // Điểm cốt tử: endpoint này ẩn danh, nên phân biệt được email có thật hay không là dò
        // được cả danh sách người dùng.
        var client = Client();
        var real = NewEmail();
        await RegisterAsync(client, real);
        var fake = NewEmail();

        var forReal = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailOnlyRequest(real));
        var forFake = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailOnlyRequest(fake));

        forReal.StatusCode.Should().Be(forFake.StatusCode);
        (await forReal.Content.ReadAsStringAsync())
            .Should().Be(await forFake.Content.ReadAsStringAsync());

        // Và không có thư nào thật sự được gửi tới địa chỉ bịa.
        _email.WasSentTo(fake).Should().BeFalse();
    }

    [Fact]
    public async Task AVerificationCodeCannotResetAPassword()
    {
        var client = Client(requireEmailVerification: true);
        var email = NewEmail();
        await RegisterAsync(client, email);
        await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new EmailOnlyRequest(email));

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(email, _email.CodeFor(email)!, NewPassword));

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        body!.Error.Code.Should().Be("AUTH_OTP_INVALID");
    }

    [Fact]
    public async Task ResetRefusesAWeakPassword()
    {
        // Nới lỏng ở đây là làm luật mạnh lúc đăng ký thành vô nghĩa — ai cũng đi đường vòng.
        var client = Client();
        var email = NewEmail();
        await RegisterAsync(client, email);
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new EmailOnlyRequest(email));

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(email, _email.CodeFor(email)!, "yeu"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
