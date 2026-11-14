using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary><c>GET /api/v1/me/data-coverage</c> (BR-LH-07) trên Postgres thật: kiểm khoảng ngày, <c>unknown</c> khi chưa báo, trạng thái từng ví, chỉ ví của chính user.</summary>
[Collection("Integration")]
public class DataCoverageControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public DataCoverageControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record Envelope(bool Success, CoverageBody Data);
    private record CoverageBody(int WindowDays, int? CoveragePercent, string Level, List<AccountBody> Accounts, List<DeviceBody> Devices);
    private record AccountBody(Guid AccountId, string AccountName, string Status, DateTimeOffset? LastNotificationAt, int? SilentDays, int DaysWithNotifications, bool NotificationsOff);
    private record DeviceBody(Guid InstallationId, string Status, bool PermissionGranted, bool ListenerConnected);

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Coverage Test User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return (body!.Data.AccessToken, await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
    }

    private async Task<T> WithDbAsync<T>(Func<FinMateDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private Task<Guid> AddAccountAsync(Guid userId, string name, string package, bool monitored = true, int createdDaysAgo = 60)
        => WithDbAsync(async db =>
        {
            var account = new FinancialAccount
            {
                Id = Guid.NewGuid(), UserId = userId, AccountName = name, AccountType = AccountType.Bank, PackageName = package, IsMonitored = monitored,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo), UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.FinancialAccounts.Add(account);
            await db.SaveChangesAsync();
            return account.Id;
        });

    private Task AddLogsAsync(Guid userId, Guid accountId, string package, IEnumerable<DateTimeOffset> times)
        => WithDbAsync(async db =>
        {
            foreach (var at in times)
            {
                db.NotificationLogs.Add(new NotificationLog
                {
                    Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = accountId, PackageName = package, ContentHash = Guid.NewGuid().ToString("N"),
                    ReceivedAt = at, Status = NotificationLogStatus.Processed, CreatedAt = at,
                });
            }

            return await db.SaveChangesAsync();
        });

    private static IEnumerable<DateTimeOffset> TwicePerDay(int days, double silentHours)
    {
        var last = DateTimeOffset.UtcNow.AddHours(-silentHours);
        return Enumerable.Range(0, days * 2).Select(i => last.AddHours(-12 * i));
    }

    private async Task<HttpResponseMessage> GetAsync(string token, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/data-coverage" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<CoverageBody> ReadAsync(string token, string query = "")
    {
        var response = await GetAsync(token, query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Envelope>(JsonOptions))!.Data;
    }

    private Task PutStatusAsync(string token, Guid installation, bool permission, object[]? packages = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/devices/listener-status")
        {
            Content = JsonContent.Create(new { installationId = installation, permissionGranted = permission, listenerConnected = true, selectedPackages = packages, appVersion = "2.4.0" }, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(91)]
    public async Task DaysOutsideSevenToNinety_Is400(int days)
    {
        var (token, _) = await RegisterAndLoginAsync();

        (await GetAsync(token, $"?days={days}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoDeviceHasReported_IsUnknown_NotPoor()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        await AddAccountAsync(userId, "VCB", "com.VCB");

        var body = await ReadAsync(token);

        body.WindowDays.Should().Be(30);
        body.CoveragePercent.Should().BeNull();
        body.Level.Should().Be("unknown");
        body.Accounts.Should().ContainSingle().Which.Status.Should().Be("not_enough_data");
    }

    [Fact]
    public async Task AccountStatuses_OnlyForWatchedAccountsOfThisUser()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var (_, otherUser) = await RegisterAndLoginAsync();
        var vcb = await AddAccountAsync(userId, "VCB", "com.VCB");
        var momo = await AddAccountAsync(userId, "MoMo", "com.mservice.momotransfer");
        await AddAccountAsync(userId, "Tắt theo dõi", "com.vnpay.bidv", monitored: false);
        await AddAccountAsync(otherUser, "Của người khác", "com.VCB");
        await AddLogsAsync(userId, vcb, "com.VCB", TwicePerDay(30, 72));
        await AddLogsAsync(userId, momo, "com.mservice.momotransfer", [DateTimeOffset.UtcNow.AddDays(-28), DateTimeOffset.UtcNow.AddDays(-19), DateTimeOffset.UtcNow.AddDays(-10)]);

        var body = await ReadAsync(token);

        body.Accounts.Select(a => a.AccountName).Should().BeEquivalentTo("VCB", "MoMo");
        var vcbBody = body.Accounts.Single(a => a.AccountId == vcb);
        vcbBody.Status.Should().Be("silent");
        vcbBody.SilentDays.Should().Be(3);
        vcbBody.DaysWithNotifications.Should().BeInRange(26, 28);
        body.Accounts.Single(a => a.AccountId == momo).Status.Should().Be("not_enough_data");
    }

    [Fact]
    public async Task ListenerStillSeesTheApp_IsAnUploadGap()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var vcb = await AddAccountAsync(userId, "VCB", "com.VCB");
        await AddLogsAsync(userId, vcb, "com.VCB", TwicePerDay(30, 96));
        await PutStatusAsync(token, Guid.NewGuid(), true, [new { packageName = "com.VCB", notificationsEnabled = false, lastPostedAt = DateTimeOffset.UtcNow.AddHours(-3) }]);

        var account = (await ReadAsync(token)).Accounts.Single();

        account.Status.Should().Be("upload_gap");
        account.NotificationsOff.Should().BeTrue();
    }

    [Fact]
    public async Task HalfTheDaysWithoutAnOkDevice_IsFiftyPercent_Poor()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        await AddAccountAsync(userId, "VCB", "com.VCB");
        var installation = Guid.NewGuid();
        await PutStatusAsync(token, installation, true);
        var today = VietnamTime.Today();
        await WithDbAsync(async db =>
        {
            var status = await db.DeviceListenerStatuses.SingleAsync(s => s.UserId == userId);
            status.FirstReportedAt = DateTimeOffset.UtcNow.AddDays(-40);
            db.DeviceListenerDays.RemoveRange(db.DeviceListenerDays.Where(d => d.StatusId == status.Id));
            for (var i = 0; i < 30; i++)
            {
                var day = today.AddDays(-i);
                var ok = i % 2 == 0;
                db.DeviceListenerDays.Add(new DeviceListenerDay
                {
                    StatusId = status.Id, UserId = userId, Day = day, AnyPermissionDenied = !ok, LastPermissionGranted = ok, LastListenerConnected = ok,
                    LastReportedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), VietnamTime.Offset), ReportCount = 1,
                });
            }

            return await db.SaveChangesAsync();
        });

        var body = await ReadAsync(token);

        body.CoveragePercent.Should().Be(50);
        body.Level.Should().Be("poor");
        body.Devices.Should().ContainSingle().Which.InstallationId.Should().Be(installation);
    }
}
