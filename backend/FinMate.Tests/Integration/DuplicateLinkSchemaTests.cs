using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ bản trùng có liên kết (Phase 82) trên Postgres thật: các CHECK chặn đúng dữ liệu sai, hai khoá ngoại tự tham chiếu mới
/// không chặn xoá cứng cả user, thông báo nhận trạng thái <c>duplicate</c>, và hoá đơn không nối được vào giao dịch đã xoá mềm.
/// Gieo bằng SQL thô để kiểm chính database, không qua mô hình EF.
/// </summary>
public class DuplicateLinkSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private readonly ReceiptScanDbFixture _fixture;

    public DuplicateLinkSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Duplicate Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'Ví', 'cash', now(), now())
            """);
        return (user, account);
    }

    private static async Task<Guid> InsertAsync(
        FinMateDbContext db, Guid user, Guid account,
        Guid? duplicateOf = null, string? duplicateStatus = null,
        Guid? mergedInto = null, bool deleted = false, string[]? mergedSources = null, Guid? id = null)
    {
        var txId = id ?? Guid.NewGuid();
        DateTimeOffset? deletedAt = deleted ? DateTimeOffset.UtcNow : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at,
                                      duplicate_of_transaction_id, duplicate_status, merged_into_transaction_id, merged_sources, deleted_at)
            VALUES ({txId}, {user}, {account}, 50000, 'debit', 'notification', 'draft', now(), now(), now(),
                    {duplicateOf}, {duplicateStatus}, {mergedInto}, {mergedSources}, {deletedAt})
            """);
        return txId;
    }

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(CheckViolation);
        thrown.Which.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task SuspectedDismissedAndMerged_AreAccepted()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var survivor = await InsertAsync(db, user, account, mergedSources: ["manual", "notification"]);

        await InsertAsync(db, user, account, survivor, "suspected");
        await InsertAsync(db, user, account, survivor, "dismissed");
        await InsertAsync(db, user, account, mergedInto: survivor, deleted: true);

        var count = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transactions WHERE user_id = {user}").SingleAsync();
        count.Should().Be(4);
    }

    [Fact]
    public async Task UnknownDuplicateStatus_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var other = await InsertAsync(db, user, account);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, other, "merged"), "chk_transactions_duplicate_status");
    }

    [Fact]
    public async Task Status_WithoutLink_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, null, "suspected"), "chk_transactions_duplicate_link");
    }

    [Fact]
    public async Task Link_WithoutStatus_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var other = await InsertAsync(db, user, account);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, other, null), "chk_transactions_duplicate_link");
    }

    [Fact]
    public async Task DuplicateOfItself_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var id = Guid.NewGuid();
        await ShouldViolateAsync(() => InsertAsync(db, user, account, id, "suspected", id: id), "chk_transactions_duplicate_not_self");
    }

    [Fact]
    public async Task MergedIntoItself_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var id = Guid.NewGuid();
        await ShouldViolateAsync(() => InsertAsync(db, user, account, mergedInto: id, deleted: true, id: id), "chk_transactions_merged_not_self");
    }

    [Fact]
    public async Task Merged_MustBeSoftDeleted()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var survivor = await InsertAsync(db, user, account);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, mergedInto: survivor), "chk_transactions_merged_deleted");
    }

    [Fact]
    public async Task UnknownMergedSource_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, mergedSources: ["manual", "sms"]), "chk_transactions_merged_sources");
    }

    [Fact]
    public async Task NotificationLog_AcceptsDuplicateStatus()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
            VALUES ({Guid.NewGuid()}, {user}, 'com.VCB', {Guid.NewGuid().ToString("N")}, 'duplicate', now(), now())
            """);

        var statuses = await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM notification_logs WHERE user_id = {user}").ToListAsync();
        statuses.Should().Equal("duplicate");
    }

    [Fact]
    public async Task DeletingUser_RemovesDuplicateAndMergeChainsWithoutForeignKeyError()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var survivor = await InsertAsync(db, user, account);
        await InsertAsync(db, user, account, survivor, "suspected");
        await InsertAsync(db, user, account, mergedInto: survivor, deleted: true);

        // Như UserHardDeleter: chỉ xoá dòng users, giao dịch đi theo cascade trong cùng một câu lệnh (FK NO ACTION kiểm ở cuối câu).
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        var left = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transactions WHERE user_id = {user}").SingleAsync();
        left.Should().Be(0);
    }

    [Fact]
    public async Task ReceiptLink_ToSoftDeletedTransaction_IsNotInserted()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var tx = await InsertAsync(db, user, account, deleted: true);
        var scan = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO receipt_scans (id, user_id, ocr_result, amount_cents, category_slug, created_at)
            VALUES ({scan}, {user}, 'success', 50000, 'food', now())
            """);

        var linked = await new ReceiptScanRepository(db).LinkToTransactionAsync(scan, user, tx, "food");

        linked.Should().BeFalse();
    }
}
