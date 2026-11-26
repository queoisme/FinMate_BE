using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ trả góp (Phase 92, migration <c>AddRecurringInstallments</c>) trên Postgres thật: CHECK của các cột mới, ràng buộc duy nhất bỏ qua dòng
/// đã hoàn thành (BR-INS-09), bảng liên kết thanh toán (một giao dịch một dòng sống, một kỳ một dòng), và suy <c>anchor_day</c> khi migrate (S2).
/// Gieo bằng SQL thô để kiểm chính database.
/// </summary>
public class RecurringInstallmentsSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string MigrationBefore = "20261125112631_AddPersonalDataExports";
    private readonly ReceiptScanDbFixture _fixture;

    public RecurringInstallmentsSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var (user, account) = (Guid.NewGuid(), Guid.NewGuid());
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Recurring Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'VCB', 'bank', now(), now())
            """);
        return (user, account);
    }

    private static async Task<Guid> InsertItemAsync(
        FinMateDbContext db, Guid user, string key = "fe credit", string status = "active", DateOnly? due = null, DateTimeOffset? lastSeen = null,
        string cadence = "monthly")
    {
        var id = Guid.NewGuid();
        var nextDue = due ?? new DateOnly(2026, 12, 15);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recurring_items (id, user_id, direction, name, counterparty_key, amount_cents, cadence, next_due_date, status, source,
                                         last_seen_at, created_at, updated_at)
            VALUES ({id}, {user}, 'outflow', 'Khoản', {key}, 1200000, {cadence}, {nextDue}, {status}, 'manual', {lastSeen}, now(), now())
            """);
        return id;
    }

    private static async Task<Guid> InsertTransactionAsync(FinMateDbContext db, Guid user, Guid account)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at)
            VALUES ({id}, {user}, {account}, 1200000, 'debit', 'manual', 'confirmed', now(), now(), now())
            """);
        return id;
    }

    private static Task InsertPaymentAsync(
        FinMateDbContext db, Guid user, Guid item, DateOnly due, Guid? transaction = null, string kind = "matched", DateTimeOffset? deletedAt = null)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recurring_item_payments (id, user_id, recurring_item_id, transaction_id, due_date, kind, created_at, deleted_at)
            VALUES ({Guid.NewGuid()}, {user}, {item}, {transaction}, {due}, {kind}, now(), {deletedAt})
            """);

    private static Task UpdateAsync(FinMateDbContext db, Guid item, string set)
        => db.Database.ExecuteSqlRawAsync("UPDATE recurring_items SET " + set + " WHERE id = {0}", item);   // chỉ hằng chuỗi trong test

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint, string state = CheckViolation)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(state);
        thrown.Which.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task ItemChecks_RejectBothEnds_AnchorOnNonMonthly_AndInconsistentCompletion()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var monthly = await InsertItemAsync(db, user, "a");
        var weekly = await InsertItemAsync(db, user, "b", cadence: "weekly");

        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "total_occurrences = 12, end_date = '2027-12-01'"), "chk_recurring_items_one_end");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "total_occurrences = 601"), "chk_recurring_items_total_occurrences");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "total_occurrences = 0"), "chk_recurring_items_total_occurrences");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "anchor_day = 32"), "chk_recurring_items_anchor_day");
        await ShouldViolateAsync(() => UpdateAsync(db, weekly, "anchor_day = 15"), "chk_recurring_items_anchor_day");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "prepaid_occurrences = -1"), "chk_recurring_items_prepaid");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "status = 'completed'"), "chk_recurring_items_completed_at");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "completed_at = now()"), "chk_recurring_items_completed_at");
        await ShouldViolateAsync(() => UpdateAsync(db, monthly, "settled_early = true"), "chk_recurring_items_settled_early");

        await UpdateAsync(db, monthly, "total_occurrences = 12, anchor_day = 31, status = 'completed', completed_at = now(), settled_early = true");
    }

    [Fact]
    public async Task UniqueCounterparty_IgnoresCompletedRows_ButStillBlocksTwoLiveOnes()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var done = await InsertItemAsync(db, user, "fe credit");
        await UpdateAsync(db, done, "total_occurrences = 12, status = 'completed', completed_at = now()");

        await InsertItemAsync(db, user, "fe credit");   // BR-INS-09: khoản mới cùng người nhận

        await ShouldViolateAsync(() => InsertItemAsync(db, user, "fe credit", status: "cancelled"), "uq_recurring_items_user_direction_counterparty", UniqueViolation);
    }

    [Fact]
    public async Task Payments_OneLiveRowPerTransaction_AndPerDueDate_SettlementAndDeletedRowsExempt()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var item = await InsertItemAsync(db, user, "x");
        var other = await InsertItemAsync(db, user, "y");
        var tx = await InsertTransactionAsync(db, user, account);
        var due = new DateOnly(2026, 12, 15);

        await InsertPaymentAsync(db, user, item, due, tx);
        await ShouldViolateAsync(() => InsertPaymentAsync(db, user, other, due, tx), "uq_recurring_item_payments_transaction", UniqueViolation);
        await ShouldViolateAsync(() => InsertPaymentAsync(db, user, item, due, kind: "manual"), "uq_recurring_item_payments_item_due", UniqueViolation);
        await ShouldViolateAsync(() => InsertPaymentAsync(db, user, item, due.AddMonths(1), kind: "bogus"), "chk_recurring_item_payments_kind");

        await InsertPaymentAsync(db, user, item, due, kind: "settlement");
        await InsertPaymentAsync(db, user, item, due.AddMonths(1), kind: "manual", deletedAt: DateTimeOffset.UtcNow);
        await InsertPaymentAsync(db, user, item, due.AddMonths(1), kind: "manual");
    }

    [Fact]
    public async Task HardDeletingTheUser_RemovesItemsAndPayments()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var item = await InsertItemAsync(db, user, "z");
        await InsertPaymentAsync(db, user, item, new DateOnly(2026, 12, 15), await InsertTransactionAsync(db, user, account));

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM recurring_item_payments WHERE user_id = {user}").SingleAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Migration_DerivesAnchorDay_FromDueDateAndLastPayment_AndDownRestoresTheOldSchema()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        var (user, _) = await SeedUserAsync(db);
        static DateTimeOffset Vn(int y, int m, int d) => new DateTimeOffset(y, m, d, 9, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();

        var day30 = await InsertItemAsync(db, user, "a", due: new DateOnly(2026, 11, 30), lastSeen: Vn(2026, 10, 30));
        var endOfMonth = await InsertItemAsync(db, user, "b", due: new DateOnly(2026, 11, 30), lastSeen: Vn(2026, 10, 31));
        var day28 = await InsertItemAsync(db, user, "c", due: new DateOnly(2027, 2, 28), lastSeen: Vn(2027, 1, 28));
        var drifted = await InsertItemAsync(db, user, "d", due: new DateOnly(2026, 12, 15), lastSeen: Vn(2026, 11, 13));
        var noPaymentEnd = await InsertItemAsync(db, user, "e", due: new DateOnly(2026, 11, 30));
        var noPayment = await InsertItemAsync(db, user, "f", due: new DateOnly(2026, 12, 15));
        var weekly = await InsertItemAsync(db, user, "g", due: new DateOnly(2026, 11, 30), cadence: "weekly");
        var lateNightUtc = await InsertItemAsync(db, user, "h", due: new DateOnly(2026, 12, 1), lastSeen: new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero));

        await migrator.MigrateAsync();

        async Task<int?> AnchorAsync(Guid id) => await db.Database.SqlQuery<int?>($"SELECT anchor_day::int AS \"Value\" FROM recurring_items WHERE id = {id}").SingleAsync();
        (await AnchorAsync(day30)).Should().Be(30, "ngày 30 có lần trả 30/10 không phải cuối tháng");
        (await AnchorAsync(endOfMonth)).Should().Be(31, "cả 31/10 và 30/11 đều là cuối tháng");
        (await AnchorAsync(day28)).Should().Be(28, "lỗi §13.1: ngày 28 đến hạn 28/02 không còn bị coi là cuối tháng");
        (await AnchorAsync(drifted)).Should().Be(15, "lấy ngày lớn hơn giữa ngày đến hạn và ngày trả");
        (await AnchorAsync(noPaymentEnd)).Should().Be(31, "không có lần trả: ngày đến hạn cuối tháng → 31 (D8)");
        (await AnchorAsync(noPayment)).Should().Be(15);
        (await AnchorAsync(weekly)).Should().BeNull();
        (await AnchorAsync(lateNightUtc)).Should().Be(1, "18:00 UTC ngày 31/10 là 01:00 ngày 01/11 giờ VN");

        var done = await InsertItemAsync(db, user, "a2");
        await UpdateAsync(db, done, "counterparty_key = 'a', total_occurrences = 12, status = 'completed', completed_at = now()");
        await migrator.MigrateAsync(MigrationBefore);
        (await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM recurring_items WHERE id = {done}").SingleAsync()).Should().Be("cancelled");
        (await db.Database.SqlQuery<bool>($"SELECT deleted_at IS NOT NULL AS \"Value\" FROM recurring_items WHERE id = {done}").SingleAsync())
            .Should().BeTrue("trùng người nhận với khoản còn sống: xoá mềm để index cũ dựng lại được");
        await migrator.MigrateAsync();
    }
}
