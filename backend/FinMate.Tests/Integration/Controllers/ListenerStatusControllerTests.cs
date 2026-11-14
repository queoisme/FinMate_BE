using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// <c>PUT /api/v1/devices/listener-status</c> (BR-LH-01/02) trên Postgres thật: lưu trạng thái mới nhất, gộp dòng theo ngày (S3),
/// mốc mất quyền, lọc package lạ (D2), mỗi user một dòng cho cùng <c>installationId</c> (D1).
/// </summary>
[Collection("Integration")]
public class ListenerStatusControllerTests : IClassFixture<AuthApiFactory>
{
    private const string Url = "/api/v1/devices/listener-status";
    private const string Momo = "com.mservice.momotransfer";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ListenerStatusControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Listener Test User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return (body!.Data.AccessToken, userId);
    }

    private Task<HttpResponseMessage> PutAsync(string? token, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(payload, options: JsonOptions) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request);
    }

    private static object Body(Guid installation, bool permission = true, bool connected = true, object[]? packages = null)
        => new { installationId = installation, permissionGranted = permission, listenerConnected = connected, selectedPackages = packages, appVersion = "2.4.0", manufacturer = "Xiaomi" };

    private async Task<(List<DeviceListenerStatus> Statuses, List<DeviceListenerDay> Days)> ReadAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return (await db.DeviceListenerStatuses.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(),
                await db.DeviceListenerDays.AsNoTracking().Where(d => d.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        (await PutAsync(null, Body(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AMissingFlag_Is400_NotReadAsPermissionLost()
    {
        var (token, userId) = await RegisterAndLoginAsync();

        var response = await PutAsync(token, new { installationId = Guid.NewGuid(), listenerConnected = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(userId)).Statuses.Should().BeEmpty();
    }

    [Fact]
    public async Task AReport_StoresTheLatestStatus_AndOneDailyRow()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var installation = Guid.NewGuid();

        var response = await PutAsync(token, Body(installation, packages:
        [
            new { packageName = Momo, notificationsEnabled = true, lastPostedAt = DateTimeOffset.UtcNow.AddHours(-2) },
            new { packageName = "com.facebook.katana", notificationsEnabled = true },
        ]));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var (statuses, days) = await ReadAsync(userId);
        var status = statuses.Should().ContainSingle().Subject;
        status.InstallationId.Should().Be(installation);
        status.Manufacturer.Should().Be("xiaomi");
        status.PermissionLostSince.Should().BeNull();
        status.SelectedPackages.Should().Contain(Momo).And.NotContain("facebook");
        days.Should().ContainSingle().Which.PermissionOk.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectThenReconnectOnTheSameDay_KeepsTheDayOk()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var installation = Guid.NewGuid();

        await PutAsync(token, Body(installation, connected: false));
        await PutAsync(token, Body(installation, connected: true));

        var day = (await ReadAsync(userId)).Days.Should().ContainSingle().Subject;
        day.ReportCount.Should().Be(2);
        day.PermissionOk.Should().BeTrue();
    }

    [Fact]
    public async Task APermissionDenialAnywhereInTheDay_MakesTheDayNotOk()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var installation = Guid.NewGuid();

        await PutAsync(token, Body(installation, permission: false));
        await PutAsync(token, Body(installation, permission: true));

        var day = (await ReadAsync(userId)).Days.Single();
        day.PermissionOk.Should().BeFalse();
        day.LastStateOk.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionLostSince_IsSetOnce_AndClearedWhenGrantedAgain()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var installation = Guid.NewGuid();

        await PutAsync(token, Body(installation, permission: false));
        var first = (await ReadAsync(userId)).Statuses.Single().PermissionLostSince;
        await PutAsync(token, Body(installation, permission: false));
        var second = (await ReadAsync(userId)).Statuses.Single().PermissionLostSince;
        await PutAsync(token, Body(installation, permission: true));
        var third = (await ReadAsync(userId)).Statuses.Single().PermissionLostSince;

        first.Should().NotBeNull();
        second.Should().Be(first);
        third.Should().BeNull();
    }

    [Fact]
    public async Task TheSameInstallationUnderTwoUsers_IsTwoRows()
    {
        var (firstToken, firstUser) = await RegisterAndLoginAsync();
        var (secondToken, secondUser) = await RegisterAndLoginAsync();
        var installation = Guid.NewGuid();

        await PutAsync(firstToken, Body(installation));
        await PutAsync(secondToken, Body(installation));

        (await ReadAsync(firstUser)).Statuses.Should().ContainSingle();
        (await ReadAsync(secondUser)).Statuses.Should().ContainSingle();
    }

    [Fact]
    public async Task TooManyDevices_Is422()
    {
        var (token, _) = await RegisterAndLoginAsync();
        for (var i = 0; i < 10; i++)
        {
            (await PutAsync(token, Body(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var response = await PutAsync(token, Body(Guid.NewGuid()));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LISTENER_TOO_MANY_DEVICES");
    }
}
