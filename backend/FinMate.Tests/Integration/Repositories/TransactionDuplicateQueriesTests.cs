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
}
