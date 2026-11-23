using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ sổ vay sau Phase 90 (Task 3, migration <c>MergeDebtBookIntoLoans</c>) trên Postgres thật: CHECK của khoản và dòng, một giao dịch một dòng
/// còn sống, xoá cứng người dùng xoá sạch sổ kể cả khi dòng trỏ tới giao dịch, và chuyển dữ liệu cũ (mỗi khoản thêm một dòng giải ngân, đã trả,
/// trạng thái). Gieo bằng SQL thô để kiểm chính database, không qua mô hình EF.
/// </summary>
public class LoanBookSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string MigrationBefore = "20261123122201_AddDebtRepaymentHint";
    private readonly ReceiptScanDbFixture _fixture;

    public LoanBookSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var (user, account) = (Guid.NewGuid(), Guid.NewGuid());
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Loan Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'VCB', 'bank', now(), now())
            """);
        return (user, account);
    }

    /// <summary>Khoản theo lược đồ CŨ (trước Phase 90): không có status / repaid_cents.</summary>
    private static async Task<Guid> InsertOldLoanAsync(FinMateDbContext db, Guid user, long principal, DateOnly start)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loans (id, user_id, direction, title, counterparty, principal_cents, interest_rate_percent_year, start_date, created_at, updated_at)
            VALUES ({id}, {user}, 'lend', 'Khoản', 'Người', {principal}, 0, {start}, now(), now())
            """);
        return id;
    }

    private static async Task<Guid> InsertLoanAsync(
        FinMateDbContext db, Guid user, long principal = 2_000_000, long repaid = 0, string status = "open", DateTimeOffset? settledAt = null,
        DateTimeOffset? writtenOffAt = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loans (id, user_id, direction, title, counterparty, principal_cents, repaid_cents, status, settled_at, written_off_at,
                               interest_rate_percent_year, start_date, created_at, updated_at)
            VALUES ({id}, {user}, 'lend', 'Khoản', 'Người', {principal}, {repaid}, {status}, {settledAt}, {writtenOffAt}, 0, current_date, now(), now())
            """);
        return id;
    }

    private static async Task<Guid> InsertEntryAsync(
        FinMateDbContext db, Guid loan, string kind = "repayment", long amount = 500_000, Guid? transaction = null, DateTimeOffset? deletedAt = null,
        Guid? interestTransaction = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loan_repayments (id, loan_id, kind, amount_cents, paid_at, transaction_id, interest_transaction_id, created_at, deleted_at)
            VALUES ({id}, {loan}, {kind}, {amount}, now(), {transaction}, {interestTransaction}, now(), {deletedAt})
            """);
        return id;
    }

    private static async Task<Guid> InsertTransactionAsync(FinMateDbContext db, Guid user, Guid account)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at)
            VALUES ({id}, {user}, {account}, 500000, 'credit', 'manual', 'confirmed', now(), now(), now())
            """);
        return id;
    }

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint, string state = CheckViolation)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(state);
        thrown.Which.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task LoanChecks_RejectInconsistentTotalsAndStatus()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);

        await ShouldViolateAsync(() => InsertLoanAsync(db, user, principal: 1_000, repaid: 2_000), "ck_loans_repaid");
        await ShouldViolateAsync(() => InsertLoanAsync(db, user, repaid: -1), "ck_loans_repaid");
        await ShouldViolateAsync(() => InsertLoanAsync(db, user, status: "paid"), "ck_loans_status");
        await ShouldViolateAsync(() => InsertLoanAsync(db, user, status: "settled"), "ck_loans_settled_at");
        await ShouldViolateAsync(() => InsertLoanAsync(db, user, settledAt: DateTimeOffset.UtcNow), "ck_loans_settled_at");
        await ShouldViolateAsync(() => InsertLoanAsync(db, user, status: "written_off"), "ck_loans_written_off_at");

        await InsertLoanAsync(db, user, principal: 0);   // khoản đang bị xoá theo các dòng
        await InsertLoanAsync(db, user, repaid: 2_000_000, status: "settled", settledAt: DateTimeOffset.UtcNow);
        await InsertLoanAsync(db, user, status: "written_off", writtenOffAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EntryChecks_KindAndInterestOnlyOnRepayment()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var loan = await InsertLoanAsync(db, user);

        await ShouldViolateAsync(() => InsertEntryAsync(db, loan, kind: "interest"), "ck_loan_repayments_kind");
        await ShouldViolateAsync(() => InsertEntryAsync(db, loan, amount: 0), "ck_loan_repayments_amount");
        var tx = await InsertTransactionAsync(db, user, account);
        await ShouldViolateAsync(() => InsertEntryAsync(db, loan, kind: "principal", interestTransaction: tx), "ck_loan_repayments_interest_only_on_repayment");
    }

    [Fact]
    public async Task OneTransaction_LinksToAtMostOneLiveEntry()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var loan = await InsertLoanAsync(db, user);
        var other = await InsertLoanAsync(db, user);
        var tx = await InsertTransactionAsync(db, user, account);

        await InsertEntryAsync(db, loan, transaction: tx, deletedAt: DateTimeOffset.UtcNow);   // dòng đã xoá không giữ chỗ
        await InsertEntryAsync(db, loan, transaction: tx);
        await ShouldViolateAsync(() => InsertEntryAsync(db, other, transaction: tx), "ux_loan_repayments_transaction", UniqueViolation);

        var interest = await InsertTransactionAsync(db, user, account);
        await InsertEntryAsync(db, loan, interestTransaction: interest);
        await ShouldViolateAsync(() => InsertEntryAsync(db, other, interestTransaction: interest), "ux_loan_repayments_interest_transaction", UniqueViolation);
    }

    [Fact]
    public async Task EntryTransaction_MustExist()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var loan = await InsertLoanAsync(db, user);

        var thrown = await FluentActions.Invoking(() => InsertEntryAsync(db, loan, transaction: Guid.NewGuid())).Should().ThrowAsync<PostgresException>();
        thrown.Which.ConstraintName.Should().Be("fk_loan_repayments_transaction");
    }

    [Fact]
    public async Task HardDeletingTheUser_RemovesTheWholeBook_EvenWithLinkedTransactions()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var loan = await InsertLoanAsync(db, user);
        await InsertEntryAsync(db, loan, transaction: await InsertTransactionAsync(db, user, account), interestTransaction: await InsertTransactionAsync(db, user, account));

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM loan_repayments WHERE loan_id = {loan}").SingleAsync()).Should().Be(0);
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM loans WHERE id = {loan}").SingleAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HardDeletingALinkedTransactionAlone_IsStillRefused()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var loan = await InsertLoanAsync(db, user);
        var tx = await InsertTransactionAsync(db, user, account);
        await InsertEntryAsync(db, loan, transaction: tx);

        var thrown = await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM transactions WHERE id = {tx}"))
            .Should().ThrowAsync<PostgresException>();
        thrown.Which.ConstraintName.Should().Be("fk_loan_repayments_transaction");
    }

    private sealed record LoanRow(long Principal, long Repaid, string Status, bool HasSettledAt);

    private sealed record EntryRow(string Kind, long Amount, DateTimeOffset PaidAt, Guid? TransactionId);

    [Fact]
    public async Task Migration_GivesEachOldLoanAPrincipalEntry_AndFillsRepaidAndStatus_AndDownRestoresTheOldBook()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        var (user, account) = await SeedUserAsync(db);
        var start = new DateOnly(2026, 9, 1);
        var open = await InsertOldLoanAsync(db, user, 2_000_000, start);
        var paidTx = await InsertTransactionAsync(db, user, account);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loan_repayments (id, loan_id, amount_cents, paid_at, transaction_id, created_at)
            VALUES ({Guid.NewGuid()}, {open}, 500000, now(), {paidTx}, now())
            """);
        var done = await InsertOldLoanAsync(db, user, 300_000, start);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loan_repayments (id, loan_id, amount_cents, paid_at, created_at) VALUES ({Guid.NewGuid()}, {done}, 300000, now(), now())
            """);

        await migrator.MigrateAsync();

        async Task<LoanRow> LoanAsync(Guid id) => await db.Database.SqlQuery<LoanRow>($"""
            SELECT principal_cents AS "Principal", repaid_cents AS "Repaid", status AS "Status", settled_at IS NOT NULL AS "HasSettledAt" FROM loans WHERE id = {id}
            """).SingleAsync();
        async Task<List<EntryRow>> EntriesAsync(Guid id) => await db.Database.SqlQuery<EntryRow>($"""
            SELECT kind AS "Kind", amount_cents AS "Amount", paid_at AS "PaidAt", transaction_id AS "TransactionId" FROM loan_repayments WHERE loan_id = {id}
            ORDER BY kind
            """).ToListAsync();

        (await LoanAsync(open)).Should().Be(new LoanRow(2_000_000, 500_000, "open", false));
        (await LoanAsync(done)).Should().Be(new LoanRow(300_000, 300_000, "settled", true));
        var entries = await EntriesAsync(open);
        entries.Select(e => (e.Kind, e.Amount, e.TransactionId)).Should().Equal(("principal", 2_000_000L, (Guid?)null), ("repayment", 500_000L, paidTx));
        entries[0].PaidAt.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(7)), "dòng giải ngân ở 00:00 giờ VN ngày bắt đầu");

        await migrator.MigrateAsync(MigrationBefore);
        (await db.Database.SqlQuery<long>($"SELECT coalesce(sum(amount_cents), 0) AS \"Value\" FROM loan_repayments WHERE loan_id = {open}").SingleAsync())
            .Should().Be(500_000, "Down bỏ dòng giải ngân, không để nó thành một lần trả");
        await migrator.MigrateAsync();
        (await LoanAsync(open)).Should().Be(new LoanRow(2_000_000, 500_000, "open", false));
    }

    [Fact]
    public async Task Migration_Stops_WhenAnOldLoanWasPaidMoreThanItsPrincipal()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);
        var (user, _) = await SeedUserAsync(db);
        var loan = await InsertOldLoanAsync(db, user, 100_000, new DateOnly(2026, 9, 1));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loan_repayments (id, loan_id, amount_cents, paid_at, created_at) VALUES ({Guid.NewGuid()}, {loan}, 200000, now(), now())
            """);

        var thrown = await FluentActions.Invoking(() => migrator.MigrateAsync()).Should().ThrowAsync<PostgresException>();
        thrown.Which.MessageText.Should().Contain("đã trả nhiều hơn gốc");
    }
}
