using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Phase 84, kịch bản đầu-cuối bắt buộc (a)–(e) trên Postgres thật và DI thật: ví im lặng, ví ít dùng, mất quyền, độ phủ kém trên dự báo,
/// và provider đổi mẫu tin. Mốc thời gian dựa trên giờ thật; các lượt "hôm sau" truyền <c>now</c> vào service/job.
/// </summary>
[Collection("Integration")]
public class ListenerHealthScenarioTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ListenerHealthScenarioTests(AuthApiFactory factory)
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
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Scenario User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var token = (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
        var userId = await InDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (token, userId);
    }

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private Task<Guid> AddAccountAsync(Guid userId, string name, string package, long balance = 0)
        => InDbAsync(async db =>
        {
            var account = new FinancialAccount
            {
                Id = Guid.NewGuid(), UserId = userId, AccountName = name, AccountType = AccountType.Bank, PackageName = package, BalanceCents = balance,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-90), UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.FinancialAccounts.Add(account);
            await db.SaveChangesAsync();
            return account.Id;
        });

    private Task<int> AddLogsAsync(Guid userId, Guid accountId, string package, IEnumerable<DateTimeOffset> times)
        => InDbAsync(db =>
        {
            foreach (var at in times)
            {
                db.NotificationLogs.Add(new NotificationLog
                {
                    Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = accountId, PackageName = package, ContentHash = Guid.NewGuid().ToString("N"),
                    ReceivedAt = at, Status = NotificationLogStatus.Processed, CreatedAt = at,
                });
            }

            return db.SaveChangesAsync();
        });

    private Task<List<ForecastAlert>> AlertsAsync(Guid userId)
        => InDbAsync(db => db.ForecastAlerts.AsNoTracking().Where(a => a.UserId == userId && a.Kind == ForecastAlertKind.ListenerHealth).OrderBy(a => a.CreatedAt).ToListAsync());

    private static string Reason(ForecastAlert alert) => JsonDocument.Parse(alert.Data).RootElement.GetProperty("reason").GetString()!;

    private Task<int> RemindAsync(Guid userId, DateTimeOffset now)
        => WithAsync<IListenerHealthReminderService, int>(async s => (await s.RunForUserAsync(userId, now)).RemindersRaised);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
    }

    [Fact]
    public async Task A_BusyAccountGoesQuietForThreeDays_OneReminder_NoneTheNextDay_NewNotificationEndsTheEpisode()
    {
        var (_, userId) = await RegisterAndLoginAsync();
        var vcb = await AddAccountAsync(userId, "VCB", "com.VCB");
        var now = DateTimeOffset.UtcNow;
        var last = now.AddHours(-72);
        await AddLogsAsync(userId, vcb, "com.VCB", Enumerable.Range(0, 60).Select(i => last.AddHours(-12 * i)));   // ~2/ngày suốt 30 ngày

        await WithAsync<ListenerHealthDailyJob, bool>(async job => { await job.RunAtAsync(now); return true; });

        var alerts = await AlertsAsync(userId);
        alerts.Should().ContainSingle();
        Reason(alerts[0]).Should().Be("account_silent");
        JsonDocument.Parse(alerts[0].Data).RootElement.GetProperty("silentDays").GetString().Should().Be("3");

        (await RemindAsync(userId, now.AddDays(1))).Should().Be(0);   // hôm sau: trong khoảng chờ
        (await AlertsAsync(userId)).Should().ContainSingle();

        var fresh = now.AddDays(1).AddHours(-1);
        await AddLogsAsync(userId, vcb, "com.VCB", [fresh]);            // thông báo mới về
        (await RemindAsync(userId, now.AddDays(1))).Should().Be(0);   // đợt kết thúc: ví ok trở lại

        // Im lặng tiếp sau thông báo mới là một đợt MỚI, đánh số lại từ 1.
        (await RemindAsync(userId, fresh.AddDays(4))).Should().Be(1);
        var latest = (await AlertsAsync(userId))[^1];
        latest.DedupKey.Should().Be($"listener_health:account:{vcb:N}:{fresh.ToUnixTimeSeconds()}:1");
    }

    [Fact]
    public async Task B_ARarelyUsedWalletQuietForTenDays_IsNotReminded()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        var momo = await AddAccountAsync(userId, "MoMo", "com.mservice.momotransfer");
        var now = DateTimeOffset.UtcNow;
        await AddLogsAsync(userId, momo, "com.mservice.momotransfer", [now.AddDays(-28), now.AddDays(-19), now.AddDays(-10)]);

        (await RemindAsync(userId, now)).Should().Be(0);

        (await AlertsAsync(userId)).Should().BeEmpty();
        var coverage = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/data-coverage", token));
        coverage.GetProperty("accounts")[0].GetProperty("status").GetString().Should().Be("not_enough_data");
    }

    [Fact]
    public async Task C_PermissionLost_OneReminder_GrantedAgain_NoMore()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        await AddAccountAsync(userId, "VCB", "com.VCB");
        var installation = Guid.NewGuid();
        object Status(bool permission) => new { installationId = installation, permissionGranted = permission, listenerConnected = permission, selectedPackages = new[] { new { packageName = "com.VCB" } }, manufacturer = "Xiaomi" };

        (await SendAsync(HttpMethod.Put, "/api/v1/devices/listener-status", token, Status(false))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RemindAsync(userId, DateTimeOffset.UtcNow)).Should().Be(1);
        var alert = (await AlertsAsync(userId)).Single();
        Reason(alert).Should().Be("permission_lost");
        JsonDocument.Parse(alert.Data).RootElement.GetProperty("manufacturer").GetString().Should().Be("xiaomi");

        (await SendAsync(HttpMethod.Put, "/api/v1/devices/listener-status", token, Status(true))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InDbAsync(db => db.DeviceListenerStatuses.AsNoTracking().SingleAsync(s => s.UserId == userId))).PermissionLostSince.Should().BeNull();
        (await RemindAsync(userId, DateTimeOffset.UtcNow.AddDays(4))).Should().Be(0);
        (await AlertsAsync(userId)).Should().ContainSingle();
    }

    [Fact]
    public async Task D_FifteenOfThirtyDaysWithoutAnOkDevice_PoorCoverage_LowConfidence_AndNoFitsPlan()
    {
        var (token, userId) = await RegisterAndLoginAsync();
        await AddAccountAsync(userId, "VCB", "com.VCB", balance: 50_000_000);
        (await SendAsync(HttpMethod.Put, "/api/v1/devices/listener-status", token,
            new { installationId = Guid.NewGuid(), permissionGranted = true, listenerConnected = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var today = VietnamTime.Today();
        await InDbAsync(async db =>
        {
            var status = await db.DeviceListenerStatuses.SingleAsync(s => s.UserId == userId);
            status.FirstReportedAt = DateTimeOffset.UtcNow.AddDays(-29).AddHours(-12);
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

        var coverage = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/data-coverage?days=30", token));
        coverage.GetProperty("coveragePercent").GetInt32().Should().Be(50);

        var cashflow = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/forecast/cashflow", token));
        cashflow.GetProperty("dataCoverage").GetProperty("level").GetString().Should().Be("poor");
        cashflow.GetProperty("confidence").GetString().Should().Be("low");
        cashflow.GetProperty("dataCoverage").GetProperty("warning").GetString().Should().Contain("50%");

        var purchase = await DataAsync(await SendAsync(HttpMethod.Post, "/api/v1/forecast/purchase-check", token,
            new PurchaseCheckRequest(100_000, null, "Cà phê", null, null, null)));
        purchase.GetProperty("verdict").GetString().Should().NotBe("fits_plan");
        purchase.GetProperty("confidence").GetString().Should().Be("low");
        purchase.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("data_coverage_poor");
        purchase.GetProperty("dataCoverage").GetProperty("level").GetString().Should().Be("poor");
    }

    private async Task<string> SeedProviderAsync(int baselineTotal, int baselineFailed, int baselineFinancial, int recentTotal, int recentFailed, int recentFinancial)
    {
        var key = "test_" + Guid.NewGuid().ToString("N")[..12];
        var package = "com.test." + key;
        var now = DateTimeOffset.UtcNow;
        await InDbAsync(async db =>
        {
            db.ProviderConfigs.Add(new ProviderConfig { Id = Guid.NewGuid(), ProviderKey = key, DisplayName = "VCB thử", PackageName = package, AccountType = AccountType.Bank, CreatedAt = now, UpdatedAt = now });
            var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user);

            void Add(DateTimeOffset at, PipelineResult result)
            {
                var log = new NotificationLog { Id = Guid.NewGuid(), UserId = user.Id, PackageName = package, ContentHash = Guid.NewGuid().ToString("N"), ReceivedAt = at, Status = NotificationLogStatus.Processed, CreatedAt = at };
                db.NotificationLogs.Add(log);
                db.AiResults.Add(new AiResult { Id = Guid.NewGuid(), NotificationLogId = log.Id, PipelineResult = result, CreatedAt = at });
            }

            for (var i = 0; i < baselineTotal; i++)
            {
                var result = i < baselineFailed ? PipelineResult.ExtractionFailed : i < baselineFailed + baselineFinancial ? PipelineResult.Financial : PipelineResult.NonFinancial;
                Add(now.AddDays(-2).AddHours(-i), result);
            }

            for (var i = 0; i < recentTotal; i++)
            {
                var result = i < recentFailed ? PipelineResult.ExtractionFailed : i < recentFailed + recentFinancial ? PipelineResult.Financial : PipelineResult.NonFinancial;
                Add(now.AddMinutes(-10 - (i * 30)), result);
            }

            return await db.SaveChangesAsync();
        });
        return key;
    }

    private Task<List<OpsIncident>> IncidentsAsync(string key)
        => InDbAsync(db => db.OpsIncidents.AsNoTracking().Where(i => i.Fingerprint == "notification_format:" + key).ToListAsync());

    [Fact]
    public async Task E_ErrorRateJumpsFromTwoToThirtyPercent_OpensOneIncident_EvenWhenTheJobRunsAgain()
    {
        var key = await SeedProviderAsync(baselineTotal: 150, baselineFailed: 3, baselineFinancial: 90, recentTotal: 40, recentFailed: 12, recentFinancial: 20);

        await WithAsync<IProviderFormatMonitor, bool>(async m => { await m.RunAsync(DateTimeOffset.UtcNow); return true; });
        await WithAsync<IProviderFormatMonitor, bool>(async m => { await m.RunAsync(DateTimeOffset.UtcNow); return true; });

        var incident = (await IncidentsAsync(key)).Should().ContainSingle().Subject;
        incident.Status.Should().Be(IncidentStatus.Open);
        incident.Source.Should().Be(IncidentSource.NotificationFormat);
        incident.Severity.Should().Be(IncidentSeverity.Normal);
        incident.Title.Should().Be("Có thể VCB thử đã đổi mẫu thông báo");
        incident.TechnicalDetail.Should().Contain("error_rate").And.Contain("12/40");
    }

    [Fact]
    public async Task E_S1_FinancialShareFallsFromSixtyToFivePercent_WithNoErrors_OpensAnIncident()
    {
        var key = await SeedProviderAsync(baselineTotal: 150, baselineFailed: 0, baselineFinancial: 90, recentTotal: 40, recentFailed: 0, recentFinancial: 2);

        await WithAsync<IProviderFormatMonitor, bool>(async m => { await m.RunAsync(DateTimeOffset.UtcNow); return true; });

        (await IncidentsAsync(key)).Should().ContainSingle().Which.TechnicalDetail.Should().Contain("financial_share").And.NotContain("error_rate");
    }
}
