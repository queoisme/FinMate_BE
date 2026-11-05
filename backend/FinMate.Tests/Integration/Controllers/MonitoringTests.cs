using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API;
using FinMate.API.Controllers.Ops;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Mỗi test dùng một dịch vụ giả có tên riêng, vì DB dùng chung cả lớp.</summary>
[Collection("Integration")]
public class MonitoringTests : IClassFixture<AuthApiFactory>
{
    private const string Api = "/api/v1/ops/monitoring";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeEmailSender _email = new();
    private readonly FakeProbe _probe;
    private readonly MonitoredService _service;
    private readonly DateTimeOffset _start = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);

    public MonitoringTests(AuthApiFactory factory)
    {
        var key = "svc_" + Guid.NewGuid().ToString("N");
        _service = new MonitoredService(key, "Dịch vụ thử " + key, "Khách bị ảnh hưởng.", IncidentSeverity.High);
        _probe = new FakeProbe(_service);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceProbe>();
            services.AddScoped<IServiceProbe>(_ => _probe);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);
        }));
        _client = _factory.CreateClient();
    }

    private sealed class FakeProbe : IServiceProbe
    {
        public FakeProbe(MonitoredService service) => Service = service;

        public MonitoredService Service { get; }
        public ProbeResult Result { get; set; } = new(MonitoredServiceState.Healthy);

        public Task<ProbeResult> ProbeAsync(CancellationToken ct = default) => Task.FromResult(Result);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly List<(string To, string Subject, string Body)> _sent = [];

        public bool Working { get; set; } = true;

        public IReadOnlyList<(string To, string Subject, string Body)> Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToList();
                }
            }
        }

        public Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
        {
            lock (_sent)
            {
                if (Working)
                {
                    _sent.Add((toEmail, subject, body));
                }

                return Task.FromResult(Working);
            }
        }
    }

    private record Account(Guid Id, string Email, string Token);

    private async Task<Account> AccountAsync(UserRole role, Action<User>? customize = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        customize?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Id, user.Email, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private async Task RunAsync(DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISystemMonitor>().RunAsync(at);
    }

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private Task<List<OpsIncident>> IncidentsAsync()
        => InDbAsync(db => db.OpsIncidents.AsNoTracking().Where(i => i.Service == _service.Key).OrderBy(i => i.FirstSeenAt).ToListAsync());

    private void Fail(string detail = "HTTP 503") => _probe.Result = new ProbeResult(MonitoredServiceState.Unhealthy, detail);

    private void Heal() => _probe.Result = new ProbeResult(MonitoredServiceState.Healthy);

    private async Task<OpsIncident> OpenIncidentAsync()
    {
        Fail();
        for (var i = 0; i < 3; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        return (await IncidentsAsync()).Single();
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

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task OneOrTwoFailuresRaiseNothing_TheThirdOpensExactlyOneIncident()
    {
        Fail();
        await RunAsync(_start);
        await RunAsync(_start.AddMinutes(1));
        (await IncidentsAsync()).Should().BeEmpty();

        await RunAsync(_start.AddMinutes(2));
        await RunAsync(_start.AddMinutes(3));

        var incident = (await IncidentsAsync()).Single();
        incident.Status.Should().Be(IncidentStatus.Open);
        incident.Severity.Should().Be(IncidentSeverity.High);
        incident.OccurrenceCount.Should().Be(2);
        incident.TechnicalDetail.Should().Be("HTTP 503");
    }

    [Fact]
    public async Task AFlappingServiceNeverRaises()
    {
        for (var i = 0; i < 10; i++)
        {
            if (i % 3 == 2)
            {
                Heal();
            }
            else
            {
                Fail();
            }

            await RunAsync(_start.AddMinutes(i));
        }

        (await IncidentsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRaisesOpenOnlyOneIncident()
    {
        var signal = new IncidentSignal("race:" + _service.Key, IncidentSource.Health, _service.Key, IncidentSeverity.High, "Trùng", null);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IIncidentService>().RaiseAsync(signal, 1, _start);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Hai lượt cùng tăng đếm: lượt sau tính lại ở phút kế tiếp. Điều cần giữ là không có sự cố thứ hai.
            }
        }));

        (await IncidentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RecoveryNeedsThreeGoodChecks_ThenClosesAutomatically()
    {
        await OpenIncidentAsync();
        Heal();

        await RunAsync(_start.AddMinutes(10));
        await RunAsync(_start.AddMinutes(11));
        (await IncidentsAsync()).Single().Status.Should().Be(IncidentStatus.Open);

        await RunAsync(_start.AddMinutes(12));

        var closed = (await IncidentsAsync()).Single();
        closed.Status.Should().Be(IncidentStatus.Resolved);
        closed.ResolvedAt.Should().Be(_start.AddMinutes(12));
        closed.ResolvedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task AProblemThatReturnsAfterClosingOpensANewIncident()
    {
        await OpenIncidentAsync();
        Heal();
        for (var i = 10; i < 13; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        Fail();
        for (var i = 20; i < 23; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        var incidents = await IncidentsAsync();
        incidents.Should().HaveCount(2);
        incidents[0].Status.Should().Be(IncidentStatus.Resolved);
        incidents[1].Status.Should().Be(IncidentStatus.Open);
    }

    [Fact]
    public async Task ADisabledServiceIsNotAFailure_AndClosesAnOpenIncident()
    {
        _probe.Result = new ProbeResult(MonitoredServiceState.Disabled);
        for (var i = 0; i < 5; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        (await IncidentsAsync()).Should().BeEmpty();

        await OpenIncidentAsync();
        _probe.Result = new ProbeResult(MonitoredServiceState.Disabled);
        await RunAsync(_start.AddMinutes(10));
        (await IncidentsAsync()).Single().Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task UrgentAlertsReachAdminsAndManagers_NotStaffOrLockedAccounts()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var manager = await AccountAsync(UserRole.OpsManager);
        var staff = await AccountAsync(UserRole.Staff);
        var user = await AccountAsync(UserRole.User);
        var lockedAdmin = await AccountAsync(UserRole.Admin, u => u.IsLocked = true);
        var deletedManager = await AccountAsync(UserRole.OpsManager, u => u.DeletedAt = DateTimeOffset.UtcNow);

        await OpenIncidentAsync();

        var to = _email.Sent.Select(e => e.To).ToList();
        to.Should().Contain([admin.Email, manager.Email]);
        to.Should().NotContain([staff.Email, user.Email, lockedAdmin.Email, deletedManager.Email]);
    }

    [Fact]
    public async Task EmailsCarryImpactButNoTechnicalDetail()
    {
        var admin = await AccountAsync(UserRole.Admin);
        const string secretDetail = "chi-tiet-ky-thuat-khong-gui-email";

        Fail(secretDetail);
        for (var i = 0; i < 3; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        _email.Sent.Where(e => e.To == admin.Email).Should().OnlyContain(e => !e.Body.Contains(secretDetail) && !e.Subject.Contains(secretDetail));
    }

    [Fact]
    public async Task RemindersFollowTheScheduleAndStopOnceAcknowledged()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var manager = await AccountAsync(UserRole.OpsManager);
        var incident = await OpenIncidentAsync();
        int CountToAdmin() => _email.Sent.Count(e => e.To == admin.Email && e.Subject.Contains(_service.Key));
        CountToAdmin().Should().Be(1);

        await RunAsync(_start.AddMinutes(10));
        CountToAdmin().Should().Be(1, "chưa tới 15 phút");

        await RunAsync(_start.AddMinutes(17));
        CountToAdmin().Should().Be(2);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/acknowledge", manager.Token));
        await RunAsync(_start.AddMinutes(40));
        CountToAdmin().Should().Be(2, "đã có người nhận");
    }

    [Fact]
    public void AfterTheFirstHourRemindersBecomeHourly()
    {
        var settings = new FinMate.Application.Admin.SystemConfig.MonitoringSettings();
        var incident = new OpsIncident { FirstSeenAt = _start, LastNotifiedAt = _start.AddMinutes(61) };

        IncidentNotifier.IsDue(incident, settings, _start.AddMinutes(80)).Should().BeFalse();
        IncidentNotifier.IsDue(incident, settings, _start.AddMinutes(122)).Should().BeTrue();
    }

    [Fact]
    public async Task AFailedEmailIsRetriedOnTheNextRun()
    {
        var admin = await AccountAsync(UserRole.Admin);
        _email.Working = false;
        var incident = await OpenIncidentAsync();
        incident.NotificationCount.Should().Be(0);
        incident.LastNotifiedAt.Should().BeNull();

        _email.Working = true;
        await RunAsync(_start.AddMinutes(4));

        _email.Sent.Should().Contain(e => e.To == admin.Email);
        (await IncidentsAsync()).Single().NotificationCount.Should().Be(1);
    }

    [Fact]
    public async Task ClosingSendsOneRecoveryEmail()
    {
        var admin = await AccountAsync(UserRole.Admin);
        await OpenIncidentAsync();
        Heal();
        for (var i = 10; i < 15; i++)
        {
            await RunAsync(_start.AddMinutes(i));
        }

        _email.Sent.Where(e => e.To == admin.Email && e.Subject.Contains("Đã khắc phục") && e.Subject.Contains(_service.Key)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnIncidentNobodyWasToldAboutSendsNoRecoveryEmail()
    {
        await AccountAsync(UserRole.Admin);
        _email.Working = false;
        await OpenIncidentAsync();
        _email.Working = true;

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIncidentService>().ResolveAsync($"health:{_service.Key}", _start.AddMinutes(4));
        }

        Heal();
        await RunAsync(_start.AddMinutes(5));

        _email.Sent.Should().NotContain(e => e.Subject.Contains(_service.Name));
    }

    [Fact]
    public async Task NormalIncidentsGoOnlyToAdminsInTheDailyDigest()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var manager = await AccountAsync(UserRole.OpsManager);
        var normal = new MonitoredService(_service.Key, "Dịch vụ phụ", "Ít ảnh hưởng.", IncidentSeverity.Normal);
        var fingerprint = "normal:" + _service.Key;
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIncidentService>().RaiseAsync(
                new IncidentSignal(fingerprint, IncidentSource.Provider, normal.Key, IncidentSeverity.Normal, "Sự cố nhỏ", null), 1, _start);
        }

        Heal();
        await RunAsync(_start.AddMinutes(1));
        _email.Sent.Should().NotContain(e => e.Body.Contains("Sự cố nhỏ"), "mức thường không báo ngay");

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIncidentNotifier>().SendDigestAsync(_start.AddHours(5));
        }

        _email.Sent.Where(e => e.Body.Contains("Sự cố nhỏ")).Select(e => e.To).Should().Contain(admin.Email).And.NotContain(manager.Email);
    }

    [Fact]
    public async Task OneBrokenSourceDoesNotStopOthersOrTheEmails()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IMonitorSource, ThrowingSource>()));
        Fail();

        for (var i = 0; i < 3; i++)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISystemMonitor>().RunAsync(_start.AddMinutes(i));
        }

        (await IncidentsAsync()).Should().ContainSingle();
        _email.Sent.Should().Contain(e => e.To == admin.Email);
    }

    private sealed class ThrowingSource : IMonitorSource
    {
        public Task RunAsync(FinMate.Application.Admin.SystemConfig.MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
            => throw new InvalidOperationException("nguồn hỏng");
    }

    [Fact]
    public async Task OnlyManagersAndAdminsReachTheApi_AndOnlyAdminsSeeTechnicalDetail()
    {
        var staff = await AccountAsync(UserRole.Staff);
        var user = await AccountAsync(UserRole.User);
        var manager = await AccountAsync(UserRole.OpsManager);
        var admin = await AccountAsync(UserRole.Admin);
        var incident = await OpenIncidentAsync();

        foreach (var account in new[] { staff, user })
        {
            (await SendAsync(HttpMethod.Get, $"{Api}/status", account.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Get, $"{Api}/incidents", account.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/acknowledge", account.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var forManager = await DataAsync(await SendAsync(HttpMethod.Get, $"{Api}/incidents/{incident.Id}", manager.Token));
        forManager.GetProperty("technicalDetail").ValueKind.Should().Be(JsonValueKind.Null);
        forManager.GetProperty("impact").GetString().Should().NotBeNullOrEmpty();

        var forAdmin = await DataAsync(await SendAsync(HttpMethod.Get, $"{Api}/incidents/{incident.Id}", admin.Token));
        forAdmin.GetProperty("technicalDetail").GetString().Should().Be("HTTP 503");

        var list = await (await SendAsync(HttpMethod.Get, $"{Api}/incidents?limit=100", manager.Token)).Content.ReadAsStringAsync();
        list.Should().Contain(incident.Id.ToString()).And.NotContain("HTTP 503");
    }

    [Fact]
    public async Task ResolvingWorksWithoutABody()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        var incident = await OpenIncidentAsync();

        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/resolve", manager.Token))).GetProperty("status").GetString().Should().Be("Resolved");
    }

    [Fact]
    public async Task AcknowledgingDuringASendDoesNotCauseDuplicateEmails()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var incident = await OpenIncidentAsync();
        var sent = _email.Sent.Count(e => e.To == admin.Email && e.Subject.Contains(_service.Key));

        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMonitoringRepository>();
            var loaded = await repository.ListOpenUrgentAsync();
            await InDbAsync(db => db.OpsIncidents.Where(i => i.Id == incident.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.TechnicalDetail, "doi")));

            // Bản đã đọc từ trước không còn khớp xmin, nhưng ghi dấu đã báo vẫn phải thành công.
            await repository.MarkNotifiedAsync(loaded.Single(i => i.Id == incident.Id).Id, _start.AddMinutes(30));
        }

        var row = (await IncidentsAsync()).Single();
        row.LastNotifiedAt.Should().Be(_start.AddMinutes(30));
        row.NotificationCount.Should().Be(2);

        await RunAsync(_start.AddMinutes(31));
        _email.Sent.Count(e => e.To == admin.Email && e.Subject.Contains(_service.Key)).Should().Be(sent);
    }

    [Fact]
    public async Task StatusShowsEachServiceAndTheActiveCount()
    {
        var manager = await AccountAsync(UserRole.OpsManager);
        await OpenIncidentAsync();

        var status = await DataAsync(await SendAsync(HttpMethod.Get, $"{Api}/status", manager.Token));

        var service = status.GetProperty("services").EnumerateArray().Single(s => s.GetProperty("service").GetString() == _service.Key);
        service.GetProperty("state").GetString().Should().Be("Unhealthy");
        service.GetProperty("consecutiveFailures").GetInt32().Should().Be(3);
        status.GetProperty("activeIncidents").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AcknowledgeKeepsTheFirstPerson_ResolveCanCarryANote_AndIsAudited()
    {
        var first = await AccountAsync(UserRole.OpsManager);
        var second = await AccountAsync(UserRole.Admin);
        var incident = await OpenIncidentAsync();

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/acknowledge", first.Token));
        var again = await DataAsync(await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/acknowledge", second.Token));
        again.GetProperty("acknowledgedByUserId").GetGuid().Should().Be(first.Id);

        var resolved = await DataAsync(await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/resolve", second.Token, new ResolveIncidentRequest("Đã đổi key")));
        resolved.GetProperty("status").GetString().Should().Be("Resolved");
        resolved.GetProperty("resolutionNote").GetString().Should().Be("Đã đổi key");

        var late = await SendAsync(HttpMethod.Post, $"{Api}/incidents/{incident.Id}/resolve", second.Token, new ResolveIncidentRequest(null));
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await InDbAsync(db => db.AuditLogs.AsNoTracking().CountAsync(a => a.UserId == first.Id && a.EventType == "Ops.Incident.Acknowledged"))).Should().Be(1);
        (await InDbAsync(db => db.AuditLogs.AsNoTracking().CountAsync(a => a.UserId == second.Id && a.EventType == "Ops.Incident.Acknowledged"))).Should().Be(0);
    }

    [Fact]
    public async Task AWorseningProblemAlertsAgainEvenIfAcknowledged()
    {
        var admin = await AccountAsync(UserRole.Admin);
        var fingerprint = "worse:" + _service.Key;
        using var scope = _factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IIncidentService>();
        await incidents.RaiseAsync(new IncidentSignal(fingerprint, IncidentSource.AppError, _service.Key, IncidentSeverity.High, "Lỗi lặp lại", null), 1, _start);
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.OpsIncidents.Where(i => i.Fingerprint == fingerprint).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, IncidentStatus.Acknowledged)
            .SetProperty(i => i.LastNotifiedAt, _start)
            .SetProperty(i => i.NotificationCount, 1));
        db.ChangeTracker.Clear();

        await incidents.RaiseAsync(new IncidentSignal(fingerprint, IncidentSource.AppError, _service.Key, IncidentSeverity.Critical, "Lỗi lặp lại", null), 1, _start.AddMinutes(5));

        var row = await db.OpsIncidents.AsNoTracking().SingleAsync(i => i.Fingerprint == fingerprint);
        row.Severity.Should().Be(IncidentSeverity.Critical);
        row.Status.Should().Be(IncidentStatus.Open);
        row.LastNotifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task ThresholdsAreConfigurableAndValidated_AndHiddenFromThePublicConfig()
    {
        var admin = await AccountAsync(UserRole.Admin);
        const string config = "/api/v1/admin/system-config";
        var version = (await DataAsync(await SendAsync(HttpMethod.Get, config, admin.Token))).GetProperty("version").GetInt32();

        (await SendAsync(HttpMethod.Put, config, admin.Token, new { expectedVersion = version, content = new { monitoring = new { probeFailureThreshold = 0 } }, note = "sai" })).IsSuccessStatusCode.Should().BeFalse();

        await DataAsync(await SendAsync(HttpMethod.Put, config, admin.Token, new { expectedVersion = version, content = new { monitoring = new { probeFailureThreshold = 7 } }, note = "doi nguong" }));
        try
        {
            var publicConfig = JsonDocument.Parse(await _client.GetStringAsync("/api/v1/app-config")).RootElement;
            publicConfig.GetProperty("data").GetProperty("config").GetProperty("monitoring").GetProperty("probeFailureThreshold").GetInt32().Should().Be(3, "ngưỡng thật không lộ ra API công khai");

            Fail();
            for (var i = 0; i < 3; i++)
            {
                await RunAsync(_start.AddMinutes(i));
            }

            (await IncidentsAsync()).Should().BeEmpty("ngưỡng đã nâng lên 7");
        }
        finally
        {
            var current = (await DataAsync(await SendAsync(HttpMethod.Get, config, admin.Token))).GetProperty("version").GetInt32();
            await DataAsync(await SendAsync(HttpMethod.Put, config, admin.Token, new { expectedVersion = current, content = new { }, note = "tra lai" }));
        }
    }
}
