using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ Phase 84 trên Postgres thật: kind <c>listener_health</c>, nguồn sự cố <c>notification_format</c>, công tắc mới mặc định bật,
/// bảng trạng thái thiết bị (unique theo user + installation, CHECK mảng JSON) và xoá theo user. Gieo bằng SQL thô để kiểm chính database.
/// </summary>
public class ListenerHealthSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private readonly ReceiptScanDbFixture _fixture;

    public ListenerHealthSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Listener Schema Test");
        return user;
    }

    private static Task InsertStatusAsync(FinMateDbContext db, Guid id, Guid user, Guid installation, string packages = "[]")
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO device_listener_statuses (id, user_id, installation_id, permission_granted, listener_connected, selected_packages, first_reported_at, last_reported_at)
            VALUES ({id}, {user}, {installation}, true, true, CAST({packages} AS jsonb), now(), now())
            """);

    [Fact]
    public async Task ListenerHealthKind_IsAccepted()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO forecast_alerts (id, user_id, kind, dedup_key, title, body, data, status, scheduled_for, created_at)
            VALUES ({Guid.NewGuid()}, {user}, 'listener_health', 'listener_health:device:x:1:1', 't', 'b', CAST({"{}"} AS jsonb), 'pending', now(), now())
            """);
    }

    [Fact]
    public async Task NotificationFormatSource_IsAccepted()
    {
        await using var db = _fixture.NewContext();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ops_incidents (id, fingerprint, source, service, severity, status, title, occurrence_count, first_seen_at, last_seen_at, notification_count)
            VALUES ({Guid.NewGuid()}, {"notification_format:" + Guid.NewGuid().ToString("N")}, 'notification_format', 'vietcombank', 'normal', 'open', 't', 1, now(), now(), 0)
            """);
    }

    [Fact]
    public async Task ListenerHealthSwitch_DefaultsToOn()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);

        var enabled = await db.Database.SqlQuery<bool>($"SELECT listener_health_alerts_enabled AS \"Value\" FROM users WHERE id = {user}").SingleAsync();

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task SameInstallationTwiceForOneUser_IsRejected_ButAnotherUserMayUseIt()
    {
        await using var db = _fixture.NewContext();
        var first = await SeedUserAsync(db);
        var second = await SeedUserAsync(db);
        var installation = Guid.NewGuid();
        await InsertStatusAsync(db, Guid.NewGuid(), first, installation);

        var thrown = await FluentActions.Awaiting(() => InsertStatusAsync(db, Guid.NewGuid(), first, installation)).Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(UniqueViolation);

        await InsertStatusAsync(db, Guid.NewGuid(), second, installation);
    }

    [Fact]
    public async Task SelectedPackagesMustBeAnArray()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);

        var thrown = await FluentActions.Awaiting(() => InsertStatusAsync(db, Guid.NewGuid(), user, Guid.NewGuid(), "{}")).Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(CheckViolation);
        thrown.Which.ConstraintName.Should().Be("chk_device_listener_statuses_packages");
    }

    [Fact]
    public async Task HardDeletingTheUser_RemovesStatusAndDailyRows()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var status = Guid.NewGuid();
        await InsertStatusAsync(db, status, user, Guid.NewGuid());
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO device_listener_daily (status_id, day, user_id, any_permission_denied, last_permission_granted, last_listener_connected, last_reported_at, report_count)
            VALUES ({status}, DATE '2026-11-14', {user}, false, true, true, now(), 1)
            """);

        // DataDeletionJob đi qua UserHardDeleter: chỉ xoá dòng users, hai bảng mới đi theo khoá ngoại cascade (listener_upload_gap_daily không có user).
        await new FinMate.Infrastructure.Persistence.Repositories.UserHardDeleter(db).HardDeleteAsync(user);

        (await db.DeviceListenerStatuses.CountAsync(s => s.UserId == user)).Should().Be(0);
        (await db.DeviceListenerDays.CountAsync(d => d.UserId == user)).Should().Be(0);
    }
}
