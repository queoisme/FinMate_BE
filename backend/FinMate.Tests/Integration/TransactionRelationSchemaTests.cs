using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ hoàn tiền / đảo giao dịch (Phase 81) trên Postgres thật: các CHECK chặn đúng dữ liệu sai, khoá ngoại tự tham chiếu
/// không chặn xoá cứng cả user (UserHardDeleter xoá dòng users, giao dịch đi theo cascade trong cùng một câu lệnh).
/// Gieo bằng SQL thô để kiểm chính database, không qua mô hình EF.
/// </summary>
public class TransactionRelationSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private readonly ReceiptScanDbFixture _fixture;

    public TransactionRelationSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Refund Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'Ví', 'cash', now(), now())
            """);
        return (user, account);
    }

    private static async Task<Guid> InsertAsync(
        FinMateDbContext db, Guid user, Guid account, string type, string? relation = null, Guid? relatedId = null, bool isReversed = false, Guid? id = null)
    {
        var txId = id ?? Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at,
                                      relation, related_transaction_id, is_reversed)
            VALUES ({txId}, {user}, {account}, 900000, {type}, 'manual', 'confirmed', now(), now(), now(), {relation}, {relatedId}, {isReversed})
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
    public async Task LinkedRefundAndReversal_AreAccepted()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var original = await InsertAsync(db, user, account, "debit", isReversed: true);

        await InsertAsync(db, user, account, "credit", "reversal", original);
        await InsertAsync(db, user, account, "credit", "refund", original);
        await InsertAsync(db, user, account, "credit", "refund");   // hoàn mồ côi (BR-RF-09)

        var count = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transactions WHERE user_id = {user} AND relation IS NOT NULL").SingleAsync();
        count.Should().Be(3);
    }

    [Fact]
    public async Task UnknownRelation_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, "credit", "chargeback"), "chk_transactions_relation");
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("transfer")]
    public async Task Relation_RequiresMoneyIn(string type)
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var original = await InsertAsync(db, user, account, "debit");
        await ShouldViolateAsync(() => InsertAsync(db, user, account, type, "refund", original), "chk_transactions_relation_money_in");
    }

    [Fact]
    public async Task RelatedTransaction_RequiresRelation()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var original = await InsertAsync(db, user, account, "debit");
        await ShouldViolateAsync(() => InsertAsync(db, user, account, "credit", null, original), "chk_transactions_related_has_relation");
    }

    [Fact]
    public async Task Transaction_CannotRelateToItself()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var id = Guid.NewGuid();
        await ShouldViolateAsync(() => InsertAsync(db, user, account, "credit", "refund", id, id: id), "chk_transactions_related_not_self");
    }

    [Fact]
    public async Task Reversal_RequiresOriginal()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, "credit", "reversal"), "chk_transactions_reversal_has_original");
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("transfer")]
    public async Task IsReversed_OnlyOnDebit(string type)
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        await ShouldViolateAsync(() => InsertAsync(db, user, account, type, isReversed: true), "chk_transactions_reversed_debit");
    }

    [Fact]
    public async Task AiResultRelationHint_OnlyKnownValues()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var log = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
            VALUES ({log}, {user}, 'vn.test', {log.ToString("N")}, 'processed', now(), now())
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ai_results (notification_log_id, pipeline_result, relation_hint, created_at) VALUES ({log}, 'financial', 'refund', now())
            """);

        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ai_results (notification_log_id, pipeline_result, relation_hint, created_at) VALUES ({log}, 'financial', 'cashback', now())
                """),
            "chk_ai_results_relation_hint");
    }

    [Fact]
    public async Task AnomalyResolution_AcceptsRefundedAndReversed()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        foreach (var resolution in new[] { "refunded", "reversed" })
        {
            var tx = await InsertAsync(db, user, account, "debit");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transaction_anomalies (user_id, transaction_id, kind, severity, amount_cents, typical_cents, ratio_percent, resolution, resolved_at, created_at)
                VALUES ({user}, {tx}, 'category_spike', 'high', 900000, 100000, 900, {resolution}, now(), now())
                """);
        }

        var count = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transaction_anomalies WHERE user_id = {user}").SingleAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task DailySummary_RefundedDefaultsToZero()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO daily_summaries (id, user_id, summary_date, created_at, updated_at) VALUES ({Guid.NewGuid()}, {user}, current_date, now(), now())
            """);

        var refunded = await db.Database.SqlQuery<long>($"SELECT total_refunded_cents AS \"Value\" FROM daily_summaries WHERE user_id = {user}").SingleAsync();
        refunded.Should().Be(0);
    }

    [Fact]
    public async Task DeletingUser_RemovesLinkedPairWithoutForeignKeyError()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var original = await InsertAsync(db, user, account, "debit");
        await InsertAsync(db, user, account, "credit", "refund", original);

        // Đúng cách UserHardDeleter làm: chỉ xoá dòng users, giao dịch đi theo cascade trong cùng câu lệnh, nên khoá ngoại
        // tự tham chiếu (NO ACTION, kiểm ở cuối câu lệnh) không bị vi phạm dù khoản gốc bị xoá trước khoản hoàn.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        var left = await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM transactions WHERE user_id = {user}").SingleAsync();
        left.Should().Be(0);
    }
}
