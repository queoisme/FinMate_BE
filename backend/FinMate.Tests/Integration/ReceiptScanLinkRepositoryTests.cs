using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>Một container Postgres cho cả lớp test; mỗi test tự tạo user/lần quét riêng nên không giẫm chân nhau.</summary>
public sealed class ReceiptScanDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("finmate_links")
        .WithUsername("finmate")
        .WithPassword("finmate")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Chuỗi kết nối tới một database khác trong cùng container (MigrateAsync tự tạo database nếu chưa có).</summary>
    public string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public FinMateDbContext NewContext(string? connectionString = null)
        => new(new DbContextOptionsBuilder<FinMateDbContext>().UseNpgsql(connectionString ?? ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

/// <summary>
/// Bảng nối <c>receipt_scan_links</c> và bảng món <c>receipt_scan_items</c> trên Postgres thật: nối nguyên tử (không nối đôi, không ném 23505),
/// cascade khi xóa, và migration chép liên kết cũ từ cột <c>receipt_scans.transaction_id</c>.
/// Gieo dữ liệu bằng SQL thô để test migration không phụ thuộc mô hình EF hiện tại.
/// </summary>
public class ReceiptScanLinkRepositoryTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261009051959_LinkReceiptScansToTransactions";
    private readonly ReceiptScanDbFixture _fixture;

    public ReceiptScanLinkRepositoryTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- gieo dữ liệu bằng SQL thô ----

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        var email = $"{user:N}@finmate.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, mission_reminders_enabled, created_at, updated_at)
            VALUES ({user}, {email}, 'Link Repo Test', 'user', true, true, true, now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'Ví', 'cash', now(), now())
            """);
        return (user, account);
    }

    private static async Task<Guid> SeedCategoryAsync(FinMateDbContext db, Guid user, string slug)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO categories (id, user_id, name, slug, created_at, updated_at) VALUES ({id}, {user}, {slug}, {slug}, now(), now())
            """);
        return id;
    }

    private static async Task<Guid> SeedTransactionAsync(FinMateDbContext db, Guid user, Guid account, Guid? category = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, category_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at)
            VALUES ({id}, {user}, {account}, {category}, 16000, 'debit', 'receipt', 'confirmed', now(), clock_timestamp(), now())
            """);
        return id;
    }

    private static async Task<Guid> SeedScanAsync(FinMateDbContext db, Guid user, string? slug = "food")
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO receipt_scans (id, user_id, ocr_result, amount_cents, category_slug, created_at)
            VALUES ({id}, {user}, 'success', 16000, {slug}, now())
            """);
        return id;
    }

    private static Task<List<string>> LinkGroupsAsync(FinMateDbContext db, Guid scanId)
        => db.Database.SqlQuery<string>($"SELECT group_category_slug AS \"Value\" FROM receipt_scan_links WHERE scan_id = {scanId}").ToListAsync();

    private static Task<int> CountAsync(FinMateDbContext db, FormattableString sql)
        => db.Database.SqlQuery<int>(sql).SingleAsync();

    // ---- repository ----

    [Fact]
    public async Task Link_WithOwnScan_ReturnsTrueAndStoresGroup()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var tx = await SeedTransactionAsync(db, user, account);

        var linked = await new ReceiptScanRepository(db).LinkToTransactionAsync(scan, user, tx, "food");

        linked.Should().BeTrue();
        var link = await db.ReceiptScanLinks.AsNoTracking().SingleAsync(l => l.TransactionId == tx);
        link.ScanId.Should().Be(scan);
        link.GroupCategorySlug.Should().Be("food");
    }

    [Fact]
    public async Task Link_SameGroupTwice_SecondReturnsFalse()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var first = await SeedTransactionAsync(db, user, account);
        var second = await SeedTransactionAsync(db, user, account);
        var repo = new ReceiptScanRepository(db);

        (await repo.LinkToTransactionAsync(scan, user, first, "food")).Should().BeTrue();
        (await repo.LinkToTransactionAsync(scan, user, second, "food")).Should().BeFalse();  // bấm Lưu hai lần: không nối thêm

        (await LinkGroupsAsync(db, scan)).Should().ContainSingle();

        // Hai request song song cho cùng một nhóm (mỗi request một DbContext như thật): đúng một bên thắng, không bên nào ném 23505.
        var parallelScan = await SeedScanAsync(db, user);
        var a = await SeedTransactionAsync(db, user, account);
        var b = await SeedTransactionAsync(db, user, account);
        await using var dbA = _fixture.NewContext();
        await using var dbB = _fixture.NewContext();
        var results = await Task.WhenAll(
            new ReceiptScanRepository(dbA).LinkToTransactionAsync(parallelScan, user, a, "food"),
            new ReceiptScanRepository(dbB).LinkToTransactionAsync(parallelScan, user, b, "food"));

        results.Count(r => r).Should().Be(1);
        (await LinkGroupsAsync(db, parallelScan)).Should().ContainSingle();
    }

    [Fact]
    public async Task Link_DifferentGroupsOfSameScan_BothSucceed()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var food = await SeedTransactionAsync(db, user, account);
        var household = await SeedTransactionAsync(db, user, account);
        var repo = new ReceiptScanRepository(db);

        (await repo.LinkToTransactionAsync(scan, user, food, "food")).Should().BeTrue();
        (await repo.LinkToTransactionAsync(scan, user, household, "household")).Should().BeTrue();

        (await LinkGroupsAsync(db, scan)).Should().BeEquivalentTo("food", "household");
    }

    [Fact]
    public async Task Link_ScanOfAnotherUser_ReturnsFalse()
    {
        await using var db = _fixture.NewContext();
        var (owner, _) = await SeedUserAsync(db);
        var (thief, thiefAccount) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, owner);
        var tx = await SeedTransactionAsync(db, thief, thiefAccount);
        var repo = new ReceiptScanRepository(db);

        (await repo.LinkToTransactionAsync(scan, thief, tx, "food")).Should().BeFalse();
        (await repo.LinkToTransactionAsync(Guid.NewGuid(), thief, tx, "food")).Should().BeFalse();  // mã không tồn tại

        (await LinkGroupsAsync(db, scan)).Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionLinkedToAtMostOneScan()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var otherScan = await SeedScanAsync(db, user);
        var tx = await SeedTransactionAsync(db, user, account);
        var repo = new ReceiptScanRepository(db);

        (await repo.LinkToTransactionAsync(scan, user, tx, "food")).Should().BeTrue();
        // Khóa chính transaction_id: nối lại (nhóm khác, hoặc lần quét khác) trả false chứ không ném 23505 ra ngoài.
        (await repo.LinkToTransactionAsync(scan, user, tx, "household")).Should().BeFalse();
        (await repo.LinkToTransactionAsync(otherScan, user, tx, "food")).Should().BeFalse();

        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM receipt_scan_links WHERE transaction_id = {tx}")).Should().Be(1);
    }

    [Fact]
    public async Task HardDeletingTransactionRemovesLinkButKeepsScanAndItems()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var tx = await SeedTransactionAsync(db, user, account);
        var repo = new ReceiptScanRepository(db);
        await repo.AddItemsAsync(scan, [new ReceiptScanItem { Position = 0, Name = "Cà phê", AmountCents = 16_000, CategorySlug = "food" }]);
        await repo.LinkToTransactionAsync(scan, user, tx, "food");

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM transactions WHERE id = {tx}");

        (await LinkGroupsAsync(db, scan)).Should().BeEmpty();
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM receipt_scans WHERE id = {scan}")).Should().Be(1);
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM receipt_scan_items WHERE scan_id = {scan}")).Should().Be(1);
    }

    [Fact]
    public async Task DeletingScanDeletesItemsAndLinks()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);
        var tx = await SeedTransactionAsync(db, user, account);
        var repo = new ReceiptScanRepository(db);
        await repo.AddItemsAsync(scan, [new ReceiptScanItem { Position = 0, Name = "Cà phê", AmountCents = 16_000 }]);
        await repo.LinkToTransactionAsync(scan, user, tx, "food");

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM receipt_scans WHERE id = {scan}");

        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM receipt_scan_items WHERE scan_id = {scan}")).Should().Be(0);
        (await LinkGroupsAsync(db, scan)).Should().BeEmpty();
        (await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM transactions WHERE id = {tx}")).Should().Be(1);  // giao dịch không bị đụng
    }

    [Fact]
    public async Task AddItems_StoresItemsWithNegativeDiscountAmount()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var scan = await SeedScanAsync(db, user);

        await new ReceiptScanRepository(db).AddItemsAsync(scan,
        [
            new ReceiptScanItem { Position = 0, Name = "Sữa tươi", AmountCents = 30_000, CategorySlug = "food", CategorySource = "model" },
            new ReceiptScanItem { Position = 1, Name = "Giảm giá", AmountCents = -5_000, ParentPosition = 0 },
        ]);

        var items = await db.ReceiptScanItems.AsNoTracking().Where(i => i.ScanId == scan).OrderBy(i => i.Position).ToListAsync();
        items.Should().HaveCount(2);
        items[0].CategorySource.Should().Be("model");
        items[1].AmountCents.Should().Be(-5_000);
        items[1].ParentPosition.Should().Be(0);
        items[1].CategorySlug.Should().BeNull();
    }

    // ---- migration ----

    [Fact]
    public async Task Migration_CopiesOldLinksIntoLinkTable()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        var (user, account) = await SeedUserAsync(db);
        var category = await SeedCategoryAsync(db, user, "food");
        var withSlug = await SeedTransactionAsync(db, user, account, category);
        var withoutSlug = await SeedTransactionAsync(db, user, account);
        var scanWithSlug = Guid.NewGuid();
        var scanWithoutSlug = Guid.NewGuid();
        var scanUnlinked = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO receipt_scans (id, user_id, ocr_result, amount_cents, category_slug, created_at, transaction_id) VALUES
                ({scanWithSlug}, {user}, 'success', 16000, 'dining', now(), {withSlug}),
                ({scanWithoutSlug}, {user}, 'success', 16000, NULL, now(), {withoutSlug}),
                ({scanUnlinked}, {user}, 'success', 16000, 'food', now(), NULL)
            """);

        await migrator.MigrateAsync();

        (await LinkGroupsAsync(db, scanWithSlug)).Should().Equal("dining");  // nhóm = slug AI đoán, không phải danh mục người dùng chọn
        (await LinkGroupsAsync(db, scanWithoutSlug)).Should().Equal("unknown");
        (await LinkGroupsAsync(db, scanUnlinked)).Should().BeEmpty();
        (await CountAsync(db, $"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'receipt_scans' AND column_name = 'transaction_id'
            """)).Should().Be(0);

        // Down rồi Up lại: cột cũ được khôi phục từ bảng nối, và liên kết vẫn còn sau khi đi lên lại.
        await migrator.MigrateAsync(MigrationBefore);
        (await db.Database.SqlQuery<Guid?>($"SELECT transaction_id AS \"Value\" FROM receipt_scans WHERE id = {scanWithSlug}").SingleAsync())
            .Should().Be(withSlug);
        (await db.Database.SqlQuery<Guid?>($"SELECT transaction_id AS \"Value\" FROM receipt_scans WHERE id = {scanUnlinked}").SingleAsync())
            .Should().BeNull();

        await migrator.MigrateAsync();
        (await LinkGroupsAsync(db, scanWithSlug)).Should().Equal("dining");
        (await LinkGroupsAsync(db, scanWithoutSlug)).Should().Equal("unknown");
    }
}
