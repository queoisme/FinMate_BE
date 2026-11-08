using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinMate.API;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Monitoring;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Bật giám sát thật qua đường log của ứng dụng: ghi một log lỗi rồi xem cái gì xuống DB.</summary>
[Collection("Integration")]
public class MonitoringErrorsTests : IClassFixture<AuthApiFactory>
{
    private const string SecretAmount = "98765432";
    private const string SecretToken = "tok_bi_mat_khong_duoc_luu";
    private const string SecretEmail = "khach.bi.mat@example.com";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeJobs _jobs = new();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public MonitoringErrorsTests(AuthApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<OpsMonitoringOptions>();
            services.AddSingleton(new OpsMonitoringOptions(true));
            services.RemoveAll<IServiceProbe>();
            services.RemoveAll<IRecurringJobReader>();
            services.AddSingleton<IRecurringJobReader>(_jobs);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new SilentEmail());
        }));
        _client = _factory.CreateClient();
    }

    private sealed class SilentEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeJobs : IRecurringJobReader
    {
        public List<RecurringJobInfo> Jobs { get; } = [];

        public IReadOnlyList<RecurringJobInfo> ReadAll() => Jobs;
    }

    private sealed class SecretFailure : Exception
    {
        public SecretFailure() : base($"Trừ {SecretAmount} của {SecretEmail} với {SecretToken}")
        {
        }
    }

    private async Task<T> InDbAsync<T>(Func<FinMateDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task RunAsync(DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISystemMonitor>().RunAsync(at);
    }

    private async Task<string> TokenAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user);
    }

    private async Task<List<OpsErrorEvent>> WaitForEventsAsync(string template, int count = 1)
    {
        for (var i = 0; i < 40; i++)
        {
            var rows = await InDbAsync(db => db.OpsErrorEvents.AsNoTracking().Where(e => e.MessageTemplate == template).ToListAsync());
            if (rows.Count >= count)
            {
                return rows;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Lỗi không xuống tới DB");
    }

    private async Task SeedErrorsAsync(string fingerprint, int count, DateTimeOffset at)
    {
        await InDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.OpsErrorEvents.Add(new OpsErrorEvent
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = at.AddSeconds(-i),
                    Service = "backend",
                    Source = IncidentSource.AppError,
                    ExceptionType = "System.InvalidOperationException",
                    SourceContext = "FinMate.Thu.Vi.Tri",
                    RequestPath = "/api/v1/thu/{id}",
                    Fingerprint = fingerprint,
                });
            }

            return await db.SaveChangesAsync();
        });
    }

    private Task<OpsIncident?> IncidentAsync(string fingerprint)
        => InDbAsync(db => db.OpsIncidents.AsNoTracking().OrderByDescending(i => i.FirstSeenAt).FirstOrDefaultAsync(i => i.Fingerprint == fingerprint));

    [Fact]
    public async Task AnErrorLogStoresOnlyTheTypeLocationAndTemplate_NeverTheValues()
    {
        var template = "Trừ tiền thất bại {Amount} cho {Email} " + Guid.NewGuid().ToString("N");
        var logger = _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FinMate.Thu.Nguon");
        Exception thrown;
        try
        {
            throw new SecretFailure();
        }
        catch (SecretFailure ex)
        {
            thrown = ex;
        }

#pragma warning disable CA2017, CA2254
        logger.LogError(thrown, template, SecretAmount, SecretEmail);
#pragma warning restore CA2017, CA2254

        var row = (await WaitForEventsAsync(template)).Single();

        row.ExceptionType.Should().Contain(nameof(SecretFailure));
        row.Service.Should().Be("backend");
        row.Source.Should().Be(IncidentSource.AppError);
        row.Fingerprint.Should().StartWith("apperror:");
        row.SourceContext.Should().Contain(nameof(MonitoringErrorsTests));

        var everything = JsonSerializer.Serialize(row);
        everything.Should().NotContain(SecretAmount).And.NotContain(SecretToken).And.NotContain(SecretEmail);
    }

    [Fact]
    public async Task WarningsAreNotStored()
    {
        var template = "Chỉ là cảnh báo " + Guid.NewGuid().ToString("N");
        var logger = _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FinMate.Thu.Nguon");
        var marker = "Mốc lỗi " + Guid.NewGuid().ToString("N");

#pragma warning disable CA2017, CA2254
        logger.LogWarning(template);
        logger.LogError(marker);
#pragma warning restore CA2017, CA2254

        await WaitForEventsAsync(marker);
        (await InDbAsync(db => db.OpsErrorEvents.AsNoTracking().CountAsync(e => e.MessageTemplate == template))).Should().Be(0);
    }

    [Fact]
    public async Task ErrorsOverTheThresholdOpenAnIncident_FewerDoNot()
    {
        var below = "apperror:" + Guid.NewGuid().ToString("N");
        var above = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(below, 9, _now);
        await SeedErrorsAsync(above, 10, _now);

        await RunAsync(_now);

        (await IncidentAsync(below)).Should().BeNull();
        var incident = await IncidentAsync(above);
        incident!.Severity.Should().Be(IncidentSeverity.High);
        incident.OccurrenceCount.Should().Be(10);
        incident.TechnicalDetail.Should().Contain("InvalidOperationException").And.Contain("/api/v1/thu/{id}");
        incident.Title.Should().NotContain("InvalidOperationException", "tiêu đề đi vào email nên không có chi tiết kỹ thuật");
    }

    [Fact]
    public async Task ErrorsFarOverTheThresholdAreCritical()
    {
        var fingerprint = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(fingerprint, 50, _now);

        await RunAsync(_now);

        (await IncidentAsync(fingerprint))!.Severity.Should().Be(IncidentSeverity.Critical);
    }

    [Fact]
    public async Task OccurrencesAreNotCountedTwiceAcrossOverlappingWindows()
    {
        var fingerprint = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(fingerprint, 10, _now);
        await RunAsync(_now);
        await RunAsync(_now.AddMinutes(1));
        (await IncidentAsync(fingerprint))!.OccurrenceCount.Should().Be(10);

        await SeedErrorsAsync(fingerprint, 2, _now.AddMinutes(2));
        await RunAsync(_now.AddMinutes(2).AddSeconds(30));
        (await IncidentAsync(fingerprint))!.OccurrenceCount.Should().Be(12);
    }

    [Fact]
    public async Task AQuietErrorClosesAutomatically()
    {
        var fingerprint = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(fingerprint, 10, _now);
        await RunAsync(_now);

        await RunAsync(_now.AddMinutes(29));
        (await IncidentAsync(fingerprint))!.Status.Should().Be(IncidentStatus.Open);

        await RunAsync(_now.AddMinutes(31));
        (await IncidentAsync(fingerprint))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task AFailedJobIsHigh_AndClosesWhenTheJobSucceedsAgain()
    {
        var id = "thu-" + Guid.NewGuid().ToString("N");
        _jobs.Jobs.Add(new RecurringJobInfo(id, FailedState.StateName, _now.AddMinutes(5)));
        await RunAsync(_now);

        var incident = await IncidentAsync($"job:failed:{id}");
        incident!.Severity.Should().Be(IncidentSeverity.High);
        incident.Source.Should().Be(IncidentSource.Job);

        _jobs.Jobs[0] = new RecurringJobInfo(id, SucceededState.StateName, _now.AddMinutes(5));
        await RunAsync(_now.AddMinutes(1));
        (await IncidentAsync($"job:failed:{id}"))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task AnOverdueJobIsNormal_AndALittleLateIsFine()
    {
        var late = "thu-" + Guid.NewGuid().ToString("N");
        var overdue = "thu-" + Guid.NewGuid().ToString("N");
        _jobs.Jobs.Add(new RecurringJobInfo(late, SucceededState.StateName, _now.AddMinutes(-5)));
        _jobs.Jobs.Add(new RecurringJobInfo(overdue, SucceededState.StateName, _now.AddMinutes(-11)));

        await RunAsync(_now);

        (await IncidentAsync($"job:overdue:{late}")).Should().BeNull();
        (await IncidentAsync($"job:overdue:{overdue}"))!.Severity.Should().Be(IncidentSeverity.Normal);
    }

    [Fact]
    public async Task AnIncidentForAJobRemovedFromTheScheduleCloses()
    {
        var id = "thu-" + Guid.NewGuid().ToString("N");
        _jobs.Jobs.Add(new RecurringJobInfo(id, FailedState.StateName, _now.AddMinutes(5)));
        await RunAsync(_now);
        (await IncidentAsync($"job:failed:{id}"))!.Status.Should().Be(IncidentStatus.Open);

        _jobs.Jobs.Clear();
        await RunAsync(_now.AddMinutes(1));

        (await IncidentAsync($"job:failed:{id}"))!.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public async Task TheMonitorJobDoesNotWatchItself()
    {
        _jobs.Jobs.Add(new RecurringJobInfo("system-monitor", FailedState.StateName, _now.AddHours(-1)));

        await RunAsync(_now);

        (await IncidentAsync("job:failed:system-monitor")).Should().BeNull();
        (await IncidentAsync("job:overdue:system-monitor")).Should().BeNull();
    }

    [Fact]
    public async Task OnlyAdminsCanReadErrorEvents_AndFiltersWork()
    {
        var fingerprint = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(fingerprint, 3, _now);
        const string url = "/api/v1/admin/monitoring/error-events";

        foreach (var role in new[] { UserRole.User, UserRole.Staff, UserRole.OpsManager })
        {
            var denied = new HttpRequestMessage(HttpMethod.Get, url);
            denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(role));
            (await _client.SendAsync(denied)).StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{url}?exceptionType=System.InvalidOperationException&limit=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(UserRole.Admin));
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(fingerprint);
    }

    [Fact]
    public async Task OldErrorsAreDeleted_RecentOnesKept()
    {
        var old = "apperror:" + Guid.NewGuid().ToString("N");
        var recent = "apperror:" + Guid.NewGuid().ToString("N");
        await SeedErrorsAsync(old, 1, DateTimeOffset.UtcNow.AddDays(-31));
        await SeedErrorsAsync(recent, 1, DateTimeOffset.UtcNow.AddDays(-29));

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FinMate.Infrastructure.BackgroundJobs.MonitoringDigestJob>().RunAsync(CancellationToken.None);
        }

        (await InDbAsync(db => db.OpsErrorEvents.AsNoTracking().CountAsync(e => e.Fingerprint == old))).Should().Be(0);
        (await InDbAsync(db => db.OpsErrorEvents.AsNoTracking().CountAsync(e => e.Fingerprint == recent))).Should().Be(1);
    }
}
