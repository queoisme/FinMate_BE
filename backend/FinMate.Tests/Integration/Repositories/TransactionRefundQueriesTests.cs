using FinMate.Application.Budgets;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Repositories;

/// <summary>
/// Truy vấn của Phase 81 trên Postgres thật: tổng đã hoàn, khoá dòng khoản gốc, tìm ứng viên gợi ý, chi ròng theo phạm vi ngân sách.
/// Mọi truy vấn phải lọc theo user: id của người khác không bao giờ được tính, được khoá, hay được gợi ý.
/// </summary>
public class TransactionRefundQueriesTests : IClassFixture<ReceiptScanDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 5, 0, 0, TimeSpan.Zero);
    private readonly ReceiptScanDbFixture _fixture;

    public TransactionRefundQueriesTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seeded(Guid User, Guid Account, Guid OtherAccount, Guid Shopping, Guid Loan);

    private static async Task<Seeded> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var shopping = Guid.NewGuid();
        var loan = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Refund Query Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'Ví', 'cash', now(), now()), ({other}, {user}, 'Ví 2', 'cash', now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO categories (id, user_id, name, slug, created_at, updated_at)
            VALUES ({shopping}, {user}, 'Mua sắm', {"shopping-" + user.ToString("N")}, now(), now()),
                   ({loan}, {user}, 'Cho vay', 'loan', now(), now())
            """);
        return new Seeded(user, account, other, shopping, loan);
    }

    private static async Task<Guid> InsertAsync(
        FinMateDbContext db, Seeded s, string type, long amount, DateTimeOffset at,
        string status = "confirmed", string? relation = null, Guid? related = null, bool reversed = false,
        Guid? account = null, Guid? category = null, string? merchant = null, bool deleted = false, Guid? user = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, category_id, amount_cents, transaction_type, source, status, merchant_name,
                                      transacted_at, created_at, updated_at, deleted_at, relation, related_transaction_id, is_reversed)
            VALUES ({id}, {user ?? s.User}, {account ?? s.Account}, {category ?? s.Shopping}, {amount}, {type}, 'manual', {status}, {merchant},
                    {at}, now(), now(), {(deleted ? Now : (DateTimeOffset?)null)}, {relation}, {related}, {reversed})
            """);
        return id;
    }

    private (FinMateDbContext Db, TransactionRepository Repo) NewRepo()
    {
        var db = _fixture.NewContext();
        return (db, new TransactionRepository(db));
    }

    [Fact]
    public async Task SumConfirmedRefunds_CountsOnlyConfirmedLiveRefundsOfThatUser()
    {
        var (db, repo) = NewRepo();
        await using var _ = db;
        var s = await SeedUserAsync(db);
        var original = await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-3));
        await InsertAsync(db, s, "credit", 200_000, Now, relation: "refund", related: original);
        var second = await InsertAsync(db, s, "credit", 100_000, Now, relation: "refund", related: original);
        await InsertAsync(db, s, "credit", 300_000, Now, status: "draft", relation: "refund", related: original);
        await InsertAsync(db, s, "credit", 400_000, Now, relation: "refund", related: original, deleted: true);

        (await repo.SumConfirmedRefundsAsync(s.User, original)).Should().Be(300_000);
        (await repo.SumConfirmedRefundsAsync(s.User, original, excludeTransactionId: second)).Should().Be(200_000);
        (await repo.SumConfirmedRefundsAsync(Guid.NewGuid(), original)).Should().Be(0);

        var batch = await repo.SumConfirmedRefundsAsync(s.User, [original, Guid.NewGuid()]);
        batch.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, long>(original, 300_000));
    }

    [Fact]
    public async Task RunWithOriginalLocked_GivesTheFreshOriginal_AndRefusesSomeoneElsesId()
    {
        var (db, repo) = NewRepo();
        await using var _ = db;
        var s = await SeedUserAsync(db);
        var original = await InsertAsync(db, s, "debit", 900_000, Now);

        var amount = await repo.RunWithOriginalLockedAsync(s.User, original, t => Task.FromResult(t.AmountCents));
        amount.Should().Be(900_000);

        var stranger = Guid.NewGuid();
        var act = () => repo.RunWithOriginalLockedAsync(stranger, original, t => Task.FromResult(0));
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RunWithOriginalLocked_SecondCallerWaitsForTheFirst()
    {
        await using var seedDb = _fixture.NewContext();
        var s = await SeedUserAsync(seedDb);
        var original = await InsertAsync(seedDb, s, "debit", 900_000, Now);

        await using var db1 = _fixture.NewContext();
        await using var db2 = _fixture.NewContext();
        var firstHasLock = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var order = new List<string>();

        var first = new TransactionRepository(db1).RunWithOriginalLockedAsync(s.User, original, async _ =>
        {
            firstHasLock.SetResult();
            await releaseFirst.Task;
            lock (order) { order.Add("first"); }
            return 0;
        });
        await firstHasLock.Task;

        var second = new TransactionRepository(db2).RunWithOriginalLockedAsync(s.User, original, _ =>
        {
            lock (order) { order.Add("second"); }
            return Task.FromResult(0);
        });

        await Task.Delay(300);
        lock (order) { order.Should().BeEmpty("the second caller must still be waiting on the row lock"); }
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        order.Should().Equal("first", "second");
    }

    [Fact]
    public async Task FindRefundCandidates_FiltersAndRanks()
    {
        var (db, repo) = NewRepo();
        await using var _ = db;
        var s = await SeedUserAsync(db);

        var shoes = await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-5), merchant: "Shopee - Giày");
        var sameWallet = await InsertAsync(db, s, "debit", 1_200_000, Now.AddDays(-1));
        var exact = await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-2), account: s.OtherAccount);
        var nearest = await InsertAsync(db, s, "debit", 950_000, Now.AddHours(-1), account: s.OtherAccount);

        // Bị loại: quá nhỏ, đã hoàn hết phần đủ dùng, đã đảo, cho vay, nháp, ngoài cửa sổ, của người khác, là tiền vào.
        await InsertAsync(db, s, "debit", 500_000, Now.AddDays(-1));
        var mostlyRefunded = await InsertAsync(db, s, "debit", 1_000_000, Now.AddDays(-1));
        await InsertAsync(db, s, "credit", 200_000, Now, relation: "refund", related: mostlyRefunded);
        await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-1), reversed: true);
        await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-1), category: s.Loan);
        await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-1), status: "draft");
        await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-61));
        await InsertAsync(db, s, "credit", 900_000, Now.AddDays(-1));
        var stranger = await SeedUserAsync(db);
        await InsertAsync(db, stranger, "debit", 900_000, Now.AddDays(-1));

        var all = await repo.FindRefundCandidatesAsync(new RefundCandidateQuery(
            s.User, 900_000, Now, 60, s.Account, null, CounterpartyKey.From("Shopee - Giày"), Limit: 10));

        all.Select(c => c.Original.Id).Should().Equal(shoes, sameWallet, exact, nearest);
        all[0].RemainingRefundableCents.Should().Be(900_000);

        var top3 = await repo.FindRefundCandidatesAsync(new RefundCandidateQuery(s.User, 900_000, Now, 60, s.Account, null, CounterpartyKey.From("Shopee - Giày"), Limit: 3));
        top3.Should().HaveCount(3);
    }

    [Fact]
    public async Task NetSpend_SubtractsRefunds_DropsReversedPairs_AndHonoursExclusions()
    {
        var (db, repo) = NewRepo();
        await using var _ = db;
        var s = await SeedUserAsync(db);
        var month = (From: new DateTimeOffset(2026, 10, 31, 17, 0, 0, TimeSpan.Zero), To: new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.Zero));

        var shoes = await InsertAsync(db, s, "debit", 900_000, Now.AddDays(-3));
        var refund = await InsertAsync(db, s, "credit", 300_000, Now, relation: "refund", related: shoes);
        var wrong = await InsertAsync(db, s, "debit", 500_000, Now.AddDays(-2), reversed: true);
        await InsertAsync(db, s, "credit", 500_000, Now, relation: "reversal", related: wrong);
        await InsertAsync(db, s, "credit", 10_000_000, Now);                       // lương: không đụng chi tiêu
        await InsertAsync(db, s, "debit", 2_000_000, Now, category: s.Loan);       // cho vay: không phải chi

        var scope = BudgetSpendScope.Personal(s.User, s.Shopping, month.From, month.To);
        (await repo.SumNetSpendAsync(scope, [])).Should().Be(600_000);
        (await repo.SumNetSpendAsync(scope, [refund])).Should().Be(900_000);
        (await repo.SumConfirmedSpendAsync(s.User, s.Shopping, month.From, month.To)).Should().Be(600_000);
        (await repo.SumConfirmedSpendAsync(s.User, null, month.From, month.To)).Should().Be(600_000);
    }

    [Fact]
    public async Task ConfirmedSpend_NeverGoesBelowZero_ButNetSpendDoes()
    {
        var (db, repo) = NewRepo();
        await using var _ = db;
        var s = await SeedUserAsync(db);
        var november = (From: new DateTimeOffset(2026, 10, 31, 17, 0, 0, TimeSpan.Zero), To: new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.Zero));

        // Mua tháng 10, hoàn tháng 11 (BR-RF-04: ghi nhận ở kỳ của ngày hoàn).
        var october = await InsertAsync(db, s, "debit", 900_000, new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await InsertAsync(db, s, "credit", 900_000, Now, relation: "refund", related: october);

        (await repo.SumNetSpendAsync(BudgetSpendScope.Personal(s.User, s.Shopping, november.From, november.To), [])).Should().Be(-900_000);
        (await repo.SumConfirmedSpendAsync(s.User, s.Shopping, november.From, november.To)).Should().Be(0);
    }
}
