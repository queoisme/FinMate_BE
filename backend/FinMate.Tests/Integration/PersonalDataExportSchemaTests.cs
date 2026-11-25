using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ xuất dữ liệu (Phase 91, migration <c>AddPersonalDataExports</c>) trên Postgres thật: một yêu cầu đang chạy mỗi user, CHECK trạng thái,
/// xoá cứng user xoá yêu cầu và file (BR-EXP-12), loại ticket mới, câu trả lời mẫu, và Down/Up.
/// </summary>
public class PersonalDataExportSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string MigrationBefore = "20261123170132_AddLoanReminders";
    private const string CannedReplyId = "9a1f0e6c-5d2b-4c8e-a7f3-91b0d4e2c591";
    private readonly ReceiptScanDbFixture _fixture;

    public PersonalDataExportSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Export Schema Test");
        return user;
    }

    private static async Task<Guid> InsertExportAsync(FinMateDbContext db, Guid user, string status = "pending", string? failureCode = null,
        DateTimeOffset? readyAt = null, DateTimeOffset? expiresAt = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO personal_data_exports (id, user_id, status, format_version, requested_at, failure_code, ready_at, expires_at, attempts, download_count, counts_toward_limit)
            VALUES ({id}, {user}, {status}, 1, now(), {failureCode}, {readyAt}, {expiresAt}, 0, 0, true)
            """);
        return id;
    }

    private static Task<int> CountAsync(FinMateDbContext db, FormattableString sql) => db.Database.SqlQuery<int>(sql).SingleAsync();

    [Fact]
    public async Task AUser_HasAtMostOneActiveRequest_ButFinishedOnesDoNotCount()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        await InsertExportAsync(db, user, "failed", failureCode: "internal_error");
        await InsertExportAsync(db, user, "expired");
        await InsertExportAsync(db, user, "pending");

        var second = await FluentActions.Invoking(() => InsertExportAsync(db, user, "running")).Should().ThrowAsync<PostgresException>();
        second.Which.SqlState.Should().Be(UniqueViolation);
    }

    [Fact]
    public async Task StatusChecks_RejectUnknownStatus_ReadyWithoutExpiry_AndFailedWithoutAReason()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);

        foreach (var bad in new Func<Task>[]
        {
            () => InsertExportAsync(db, user, "done"),
            () => InsertExportAsync(db, user, "ready"),
            () => InsertExportAsync(db, user, "failed"),
            () => InsertExportAsync(db, user, "expired", failureCode: "internal_error"),
        })
        {
            (await bad.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
        }
    }

    [Fact]
    public async Task HardDeletingTheUser_RemovesRequestsAndArchives()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var export = await InsertExportAsync(db, user, "ready", readyAt: DateTimeOffset.UtcNow, expiresAt: DateTimeOffset.UtcNow.AddHours(72));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO personal_data_export_archives (export_id, data, nonce, created_at) VALUES ({export}, {new byte[] { 1, 2 }}, {new byte[] { 3 }}, now())
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM personal_data_exports WHERE user_id = {user}")).Should().Be(0);
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM personal_data_export_archives WHERE export_id = {export}")).Should().Be(0);
    }

    [Fact]
    public async Task Migration_AddsTheTicketCategoryAndCannedReply_AndDownPutsSuchTicketsBackToQuestion()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var user = await SeedUserAsync(db);
        var ticket = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO support_tickets (id, requester_user_id, category, priority, status, subject, first_response_due_at, resolve_due_at, created_at, updated_at)
            VALUES ({ticket}, {user}, 'personal_data_request', 'p2', 'new', 'Xin dữ liệu', now(), now(), now(), now())
            """);
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM canned_replies WHERE id = {Guid.Parse(CannedReplyId)} AND is_active")).Should().Be(1);

        await migrator.MigrateAsync(MigrationBefore);

        (await db.Database.SqlQuery<string>($"SELECT category AS \"Value\" FROM support_tickets WHERE id = {ticket}").SingleAsync()).Should().Be("question");
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM canned_replies WHERE id = {Guid.Parse(CannedReplyId)}")).Should().Be(0);
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = 'personal_data_exports'")).Should().Be(0);

        await migrator.MigrateAsync();
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM canned_replies WHERE id = {Guid.Parse(CannedReplyId)}")).Should().Be(1);
    }
}
