using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Repositories;

/// <summary>
/// Truy vấn của Phase 82 trên Postgres thật: nạp ứng viên trùng thô (lọc theo user, bỏ khoản đã xoá, chuyển khoản, khoản hoàn) và khoá
/// nhiều dòng giao dịch theo id tăng dần (dòng đã xoá mềm vẫn khoá được; id của người khác → 404).
/// </summary>
public class TransactionDuplicateQueriesTests : IClassFixture<ReceiptScanDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 5, 0, 0, TimeSpan.Zero);
    private readonly ReceiptScanDbFixture _fixture;

    public TransactionDuplicateQueriesTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seeded(Guid User, Guid Account, Guid OtherAccount);

    private static async Task<Seeded> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Duplicate Query Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'VCB', 'bank', now(), now()), ({other}, {user}, 'Tiền mặt', 'cash', now(), now())
            """);
        return new Seeded(user, account, other);
    }

    private static async Task<Guid> InsertAsync(
        FinMateDbContext db, Seeded s, string type = "debit", long amount = 50_000, DateTimeOffset? at = null,
        string source = "manual", string? relation = null, Guid? related = null, bool deleted = false,
        Guid? duplicateOf = null, string? duplicateStatus = null, Guid? notificationLog = null, long? balance = null, Guid? counter = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, counter_account_id, amount_cents, transaction_type, source, status, merchant_name,
                                      transacted_at, created_at, updated_at, deleted_at, relation, related_transaction_id,
                                      duplicate_of_transaction_id, duplicate_status, notification_log_id, balance_after_cents)
            VALUES ({id}, {s.User}, {s.Account}, {counter}, {amount}, {type}, {source}, 'confirmed', 'Highlands',
                    {at ?? Now}, now(), now(), {(deleted ? Now : (DateTimeOffset?)null)}, {relation}, {related},
                    {duplicateOf}, {duplicateStatus}, {notificationLog}, {balance})
            """);
        return id;
    }

    private static async Task<Guid> InsertLogAsync(FinMateDbContext db, Seeded s, string package)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
            VALUES ({id}, {s.User}, {package}, {id.ToString("N")}, 'processed', now(), now())
            """);
        return id;
    }

    private static DuplicateCandidateQuery Query(Seeded s, params long[] amounts)
        => new(s.User, amounts, Now.AddHours(-48), Now.AddHours(48));

    [Fact]
    public async Task Candidates_KeepOnlyLiveDebitsAndCreditsOfThatUserWithoutRelation()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var other = await SeedUserAsync(db);
        var debit = await InsertAsync(db, s);
        var credit = await InsertAsync(db, s, type: "credit");
        await InsertAsync(db, s, deleted: true);
        await InsertAsync(db, s, type: "transfer", counter: s.OtherAccount);
        var original = await InsertAsync(db, s, amount: 900_000);
        await InsertAsync(db, s, type: "credit", relation: "refund", related: original);
        await InsertAsync(db, s, amount: 50_001);
        await InsertAsync(db, s, at: Now.AddHours(-49));
        await InsertAsync(db, other);

        var rows = await new TransactionRepository(db).ListDuplicateCandidatesAsync(Query(s, 50_000));

        rows.Select(r => r.Probe.Id).Should().BeEquivalentTo([debit, credit]);
    }

    [Fact]
    public async Task Candidates_CarryPackageBalanceAndTheirOwnDuplicateLink()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var log = await InsertLogAsync(db, s, "com.VCB");
        var bank = await InsertAsync(db, s, source: "notification", notificationLog: log, balance: 1_000_000);
        var dismissed = await InsertAsync(db, s, source: "notification", duplicateOf: bank, duplicateStatus: "dismissed");

        var rows = await new TransactionRepository(db).ListDuplicateCandidatesAsync(Query(s, 50_000));

        var bankRow = rows.Single(r => r.Probe.Id == bank);
        bankRow.Probe.PackageName.Should().Be("com.VCB");
        bankRow.Probe.BalanceAfterCents.Should().Be(1_000_000);
        bankRow.Probe.NotificationLogId.Should().Be(log);
        bankRow.Probe.Source.Should().Be(TransactionSource.Notification);
        var dismissedRow = rows.Single(r => r.Probe.Id == dismissed);
        dismissedRow.DuplicateOfTransactionId.Should().Be(bank);
        dismissedRow.DuplicateStatus.Should().Be(DuplicateStatus.Dismissed);
    }

    [Fact]
    public async Task Candidates_SeveralAmountsInOneQuery()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var a = await InsertAsync(db, s, amount: 35_000);
        var b = await InsertAsync(db, s, amount: 50_000);

        var rows = await new TransactionRepository(db).ListDuplicateCandidatesAsync(Query(s, 35_000, 50_000, 35_000));

        rows.Select(r => r.Probe.Id).Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public async Task Lock_ReturnsBothRows_IncludingASoftDeletedOne()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var live = await InsertAsync(db, s);
        var deleted = await InsertAsync(db, s, deleted: true);

        var seen = await new TransactionRepository(db).RunWithTransactionsLockedAsync(s.User, [deleted, live],
            rows => Task.FromResult(rows));

        seen.Keys.Should().BeEquivalentTo([live, deleted]);
        seen[deleted].DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Lock_OtherUsersRow_IsNotFound()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var other = await SeedUserAsync(db);
        var mine = await InsertAsync(db, s);
        var theirs = await InsertAsync(db, other);

        var act = () => new TransactionRepository(db).RunWithTransactionsLockedAsync(s.User, [mine, theirs], _ => Task.FromResult(0));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Lock_HoldsTheRowsUntilWorkCommits()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var a = await InsertAsync(db, s);
        var b = await InsertAsync(db, s);
        var release = new TaskCompletionSource();
        var locked = new TaskCompletionSource();

        var holder = new TransactionRepository(db).RunWithTransactionsLockedAsync(s.User, [a, b], async _ =>
        {
            locked.SetResult();
            await release.Task;
            return 0;
        });
        await locked.Task;

        // Lượt thứ hai khoá cùng cặp theo thứ tự ngược lại: phải chờ, không được vào trước.
        await using var db2 = _fixture.NewContext();
        var second = new TransactionRepository(db2).RunWithTransactionsLockedAsync(s.User, [b, a], _ => Task.FromResult(1));
        var finishedEarly = await Task.WhenAny(second, Task.Delay(500)) == second;

        release.SetResult();
        await holder;
        (await second).Should().Be(1);
        finishedEarly.Should().BeFalse();
    }

    /// <summary>
    /// Phase 90 (Q14): một giao dịch 2.060.000 đã tách thành 2.000.000 (gắn khoản vay) + 60.000 lãi vẫn là MỘT sự kiện ngân hàng. Ứng viên trùng mang
    /// "số tiền ngân hàng" 2.060.000; chính giao dịch lãi không là ứng viên.
    /// </summary>
    [Fact]
    public async Task Candidates_ATransactionSplitIntoLoanAndInterest_IsFoundAtItsBankAmount()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedUserAsync(db);
        var principal = await InsertAsync(db, s, amount: 2_000_000);
        var interest = await InsertAsync(db, s, amount: 60_000);
        var loan = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loans (id, user_id, direction, title, counterparty, principal_cents, repaid_cents, status, settled_at, interest_rate_percent_year,
                               start_date, created_at, updated_at)
            VALUES ({loan}, {s.User}, 'borrow', 'Khoản', 'Người', 2000000, 2000000, 'settled', now(), 12, current_date, now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO loan_repayments (id, loan_id, kind, amount_cents, paid_at, transaction_id, interest_transaction_id, created_at)
            VALUES ({Guid.NewGuid()}, {loan}, 'repayment', 2000000, now(), {principal}, {interest}, now())
            """);
        var repo = new TransactionRepository(db);

        var atBank = await repo.ListDuplicateCandidatesAsync(Query(s, 2_060_000));
        var atInterest = await repo.ListDuplicateCandidatesAsync(Query(s, 60_000));
        var atPrincipal = await repo.ListDuplicateCandidatesAsync(Query(s, 2_000_000));

        atBank.Select(r => (r.Probe.Id, r.Probe.AmountCents)).Should().Equal((principal, 2_060_000L));
        atInterest.Should().BeEmpty("phần lãi đã tách không phải sự kiện ngân hàng riêng");
        atPrincipal.Should().BeEmpty("sự kiện ngân hàng là 2.060.000, không phải 2.000.000");
    }
}
