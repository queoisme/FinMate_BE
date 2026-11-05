using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinMate.API;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Khi bật xác thực 2 lớp, mật khẩu một mình không mở được API nội bộ nào, còn khách dùng app không bị ảnh hưởng.</summary>
[Collection("Integration")]
public class OpsTwoFactorTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "Password123!";
    private const string OpsMe = "/api/v1/ops/me";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeEmailSender _email = new();
    private readonly HttpClient _client;

    public OpsTwoFactorTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<OpsAuthOptions>();
            services.AddSingleton(new OpsAuthOptions(true));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);
        }));
        _client = _factory.CreateClient();
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
        {
            lock (_last)
            {
                _last[toEmail] = body;
            }

            return Task.FromResult(true);
        }

        public string CodeFor(string email)
        {
            lock (_last)
            {
                return Regex.Match(_last[email], @"\b\d{6}\b").Value;
            }
        }
    }

    private record TokenData(string AccessToken, string RefreshToken);
    private record TokenBody(TokenData Data);
    private record IdData(Guid Id);
    private record IdBody(IdData Data);
    private record Session(string Email, Guid Id, string AccessToken, string RefreshToken);

    private async Task<TokenData> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data;
    }

    private async Task<Session> SessionAsync(UserRole role)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Người"));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role));
        }

        var tokens = await LoginAsync(email);
        var me = await SendAsync(HttpMethod.Get, "/api/v1/users/me", tokens.AccessToken);
        var id = (await me.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
        return new Session(email, id, tokens.AccessToken, tokens.RefreshToken);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private async Task<TokenData> VerifiedAsync(Session session)
    {
        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var response = await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/verify", session.AccessToken, new { code = _email.CodeFor(session.Email), refreshToken = session.RefreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data;
    }

    private async Task<TokenData> RefreshAsync(string refreshToken)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data;
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.OpsManager)]
    [InlineData(UserRole.Admin)]
    public async Task APasswordAloneOpensNoInternalApi(UserRole role)
    {
        var session = await SessionAsync(role);

        (await SendAsync(HttpMethod.Get, OpsMe, session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/tickets", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/users", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var status = JsonDocument.Parse(await (await SendAsync(HttpMethod.Get, "/api/v1/ops/auth/status", session.AccessToken)).Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        status.GetProperty("required").GetBoolean().Should().BeTrue();
        status.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task TheRightCodeOpensTheApi_AndAdminStaysAdminOnly()
    {
        var staff = await SessionAsync(UserRole.Staff);
        var verifiedStaff = await VerifiedAsync(staff);
        (await SendAsync(HttpMethod.Get, OpsMe, verifiedStaff.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/users", verifiedStaff.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        verifiedStaff.RefreshToken.Should().Be(staff.RefreshToken);

        var admin = await SessionAsync(UserRole.Admin);
        var verifiedAdmin = await VerifiedAsync(admin);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/users", verifiedAdmin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWrongOrReusedCodeIsRefused()
    {
        var staff = await SessionAsync(UserRole.Staff);
        await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", staff.AccessToken);
        var code = _email.CodeFor(staff.Email);
        var wrong = code == "000000" ? "111111" : "000000";

        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/verify", staff.AccessToken, new { code = wrong, refreshToken = staff.RefreshToken })).IsSuccessStatusCode.Should().BeFalse();
        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/verify", staff.AccessToken, new { code, refreshToken = staff.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/verify", staff.AccessToken, new { code, refreshToken = staff.RefreshToken })).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task ACodeCannotBeRedeemedWithSomeoneElsesRefreshToken()
    {
        var first = await SessionAsync(UserRole.Staff);
        var second = await SessionAsync(UserRole.Staff);
        await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", first.AccessToken);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/verify", first.AccessToken, new { code = _email.CodeFor(first.Email), refreshToken = second.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshKeepsVerificationFor12HoursButNotBeyond()
    {
        var staff = await SessionAsync(UserRole.Staff);
        var verified = await VerifiedAsync(staff);

        var renewed = await RefreshAsync(verified.RefreshToken);
        (await SendAsync(HttpMethod.Get, OpsMe, renewed.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Đổi token không được kéo dài thời hạn: mốc vẫn là lần nhập mã thật.
        var firstMark = await MarkAsync(staff.Id);
        var renewedAgain = await RefreshAsync(renewed.RefreshToken);
        (await MarkAsync(staff.Id)).Should().Be(firstMark);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.RefreshTokens.Where(r => r.UserId == staff.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.OpsVerifiedAt, DateTimeOffset.UtcNow.AddHours(-13)));
        }

        var stale = await RefreshAsync(renewedAgain.RefreshToken);
        (await SendAsync(HttpMethod.Get, OpsMe, stale.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<DateTimeOffset?> MarkAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.RefreshTokens.AsNoTracking().Where(r => r.UserId == userId && r.RevokedAt == null).Select(r => r.OpsVerifiedAt).SingleAsync();
    }

    [Fact]
    public async Task OrdinaryCustomersNeedNoCodeAndCannotUseTheOpsAuthApi()
    {
        var customer = await SessionAsync(UserRole.User);

        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", customer.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/ops/auth/status", customer.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/v1/ops/auth/otp/send", customer.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InternalAccountsStillUseTheirPersonalAppFeaturesWithoutACode()
    {
        var staff = await SessionAsync(UserRole.Staff);

        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", staff.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADemotedStaffLosesAccessEvenWithAVerifiedToken()
    {
        var staff = await SessionAsync(UserRole.Staff);
        var verified = await VerifiedAsync(staff);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Users.Where(u => u.Id == staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.User));
        }

        (await SendAsync(HttpMethod.Get, OpsMe, verified.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
