using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ ghép cặp chuyển ví (Phase 83) trên Postgres thật: gợi ý <c>transfer</c> và loại chuyển trong <c>ai_results</c>, lý do đóng bất thường
/// <c>transfer</c>, cột bác gợi ý, bảng ảnh chụp lúc gộp (CHECK, xoá theo user). Gieo bằng SQL thô để kiểm chính database.
/// </summary>
public class TransferPairingSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private readonly ReceiptScanDbFixture _fixture;

    public TransferPairingSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account, Guid Log)> SeedAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        var log = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Transfer Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at) VALUES ({account}, {user}, 'VCB', 'bank', now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
            VALUES ({log}, {user}, 'com.VCB', {log.ToString("N")}, 'processed', now(), now())
            """);
        return (user, account, log);
    }

    private static Task InsertAiResultAsync(FinMateDbContext db, Guid log, string? hint, string? kind)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_results (id, notification_log_id, pipeline_result, is_potential_duplicate, relation_hint, transfer_kind, created_at)
            VALUES ({Guid.NewGuid()}, {log}, 'financial', false, {hint}, {kind}, now())
            """);

    private static Task InsertSnapshotAsync(FinMateDbContext db, Guid user, string kind = "transfer_pair", string? transferKind = null, string before = "{}")
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transaction_merge_snapshots (id, user_id, survivor_id, merged_id, kind, transfer_kind, before, created_at)
            VALUES ({Guid.NewGuid()}, {user}, {Guid.NewGuid()}, {Guid.NewGuid()}, {kind}, {transferKind}, CAST({before} AS jsonb), now())
            """);

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(CheckViolation);
        thrown.Which.ConstraintName.Should().Be(constraint);
    }

    [Theory]
    [InlineData("atm_withdrawal")]
    [InlineData("atm_deposit")]
    [InlineData("wallet_topup")]
    [InlineData("wallet_withdrawal")]
    [InlineData("own_account")]
    public async Task TransferHintWithEachKind_IsAccepted(string kind)
    {
        await using var db = _fixture.NewContext();
        var (_, _, log) = await SeedAsync(db);

        await InsertAiResultAsync(db, log, "transfer", kind);
    }

    [Fact]
    public async Task UnknownKind_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (_, _, log) = await SeedAsync(db);
        await ShouldViolateAsync(() => InsertAiResultAsync(db, log, "transfer", "lottery"), "chk_ai_results_transfer_kind");
    }

    [Fact]
    public async Task KindWithoutTransferHint_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (_, _, log) = await SeedAsync(db);
        await ShouldViolateAsync(() => InsertAiResultAsync(db, log, "refund", "wallet_topup"), "chk_ai_results_transfer_kind");
    }

    [Fact]
    public async Task UnknownHint_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (_, _, log) = await SeedAsync(db);
        await ShouldViolateAsync(() => InsertAiResultAsync(db, log, "gift", null), "chk_ai_results_relation_hint");
    }

    [Fact]
    public async Task AnomalyClosedAsTransfer_IsAccepted()
    {
        await using var db = _fixture.NewContext();
        var (user, account, _) = await SeedAsync(db);
        var tx = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at, transfer_dismissed_at)
            VALUES ({tx}, {user}, {account}, 500000, 'debit', 'notification', 'confirmed', now(), now(), now(), now())
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transaction_anomalies (id, user_id, transaction_id, kind, severity, amount_cents, typical_cents, ratio_percent, resolution, resolved_at, created_at)
            VALUES ({Guid.NewGuid()}, {user}, {tx}, 'duplicate', 'high', 500000, 500000, 100, 'transfer', now(), now())
            """);
    }

    [Fact]
    public async Task Snapshot_KindTransferKindAndBefore_AreChecked()
    {
        await using var db = _fixture.NewContext();
        var (user, _, _) = await SeedAsync(db);

        await InsertSnapshotAsync(db, user, "duplicate_merge");
        await InsertSnapshotAsync(db, user, "transfer_convert", "atm_withdrawal", """{"rows":[]}""");
        await ShouldViolateAsync(() => InsertSnapshotAsync(db, user, "undo"), "chk_transaction_merge_snapshots_kind");
        await ShouldViolateAsync(() => InsertSnapshotAsync(db, user, transferKind: "lottery"), "chk_transaction_merge_snapshots_transfer_kind");
        await ShouldViolateAsync(() => InsertSnapshotAsync(db, user, before: "[]"), "chk_transaction_merge_snapshots_before");
    }

    [Fact]
    public async Task DeletingUser_RemovesTheirSnapshots()
    {
        await using var db = _fixture.NewContext();
        var (user, _, _) = await SeedAsync(db);
        await InsertSnapshotAsync(db, user);

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        var left = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transaction_merge_snapshots WHERE user_id = {user}").SingleAsync();
        left.Should().Be(0);
    }
}
