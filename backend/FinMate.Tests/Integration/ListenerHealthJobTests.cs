using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>Job 09:00 trên Postgres thật, qua hộp thư và dispatcher thật: chạy hai lần trong ngày chỉ nhắc một lần; dọn đúng dòng quá hạn.</summary>
[Collection("Integration")]
public class ListenerHealthJobTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ListenerHealthJobTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Job Test User"));
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private async Task WithDbAsync(Func<FinMateDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<FinMateDbContext>());
    }

    private async Task RunJobAsync(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ListenerHealthDailyJob>().RunAtAsync(now);
    }

    [Fact]
    public async Task RunningTwiceTheSameDay_RemindsOnce()
    {
        var userId = await RegisterAsync();
        var now = DateTimeOffset.UtcNow;
        await WithDbAsync(async db =>
        {
            var account = new FinancialAccount
            {
                Id = Guid.NewGuid(), UserId = userId, AccountName = "VCB", AccountType = AccountType.Bank, PackageName = "com.VCB",
                CreatedAt = now.AddDays(-60), UpdatedAt = now,
            };
            db.FinancialAccounts.Add(account);
            var last = now.AddHours(-72);
            for (var i = 0; i < 60; i++)
            {
                var at = last.AddHours(-12 * i);
                db.NotificationLogs.Add(new NotificationLog
                {
                    Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = account.Id, PackageName = "com.VCB", ContentHash = Guid.NewGuid().ToString("N"),
                    ReceivedAt = at, Status = NotificationLogStatus.Processed, CreatedAt = at,
                });
            }

            await db.SaveChangesAsync();
        });

        await RunJobAsync(now);
        await RunJobAsync(now.AddMinutes(1));

        await WithDbAsync(async db =>
        {
            var alerts = await db.ForecastAlerts.Where(a => a.UserId == userId && a.Kind == ForecastAlertKind.ListenerHealth).ToListAsync();
            alerts.Should().ContainSingle().Which.DedupKey.Should().StartWith("listener_health:account:");
        });
    }

    [Fact]
    public async Task OldDailyRows_ArePurged_RecentOnesKept()
    {
        var userId = await RegisterAsync();
        var status = Guid.NewGuid();
        var today = FinMate.Application.Common.VietnamTime.Today();
        await WithDbAsync(async db =>
        {
            db.DeviceListenerStatuses.Add(new DeviceListenerStatus
            {
                Id = status, UserId = userId, InstallationId = Guid.NewGuid(), PermissionGranted = true, ListenerConnected = true,
                FirstReportedAt = DateTimeOffset.UtcNow.AddDays(-200), LastReportedAt = DateTimeOffset.UtcNow,
            });
            foreach (var age in new[] { 91, 90, 5 })
            {
                db.DeviceListenerDays.Add(new DeviceListenerDay
                {
                    StatusId = status, UserId = userId, Day = today.AddDays(-age), LastPermissionGranted = true, LastListenerConnected = true,
                    LastReportedAt = DateTimeOffset.UtcNow.AddDays(-age), ReportCount = 1,
                });
            }

            await db.SaveChangesAsync();
        });

        await RunJobAsync(DateTimeOffset.UtcNow);

        await WithDbAsync(async db =>
            (await db.DeviceListenerDays.Where(d => d.StatusId == status).Select(d => d.Day).ToListAsync())
                .Should().BeEquivalentTo(new[] { today.AddDays(-90), today.AddDays(-5) }));
    }
}
