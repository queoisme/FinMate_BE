using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FinMate.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Câu SQL điền cột dòng tiền cho <c>daily_summaries</c> cũ (Phase 90, Q16) phải ra đúng số mà <c>CashFlowCalculator</c> tính trên cùng dữ liệu.
/// Gieo bằng SQL thô: chi, thu, hoàn, cặp đảo, chuyển ví, vay / trả nợ, góp / rút quỹ, nháp (bị bỏ) và giao dịch đã xoá (bị bỏ).
/// </summary>
public class DailySummaryCashFlowBackfillTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public DailySummaryCashFlowBackfillTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly DateOnly Day = new(2026, 11, 2);

    [Fact]
    public async Task TheBackfill_MatchesTheCalculator()
    {
        await using var db = _fixture.NewContext();
        var (user, vcb, momo, loan, entryOut, entryIn) = await SeedAsync(db);
        // 23:30 giờ VN = 16:30 UTC cùng ngày; 00:30 giờ VN ngày 2 = 17:30 UTC ngày 1: kiểm ranh giới ngày theo giờ VN.
        var at = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(7));
        var earlyMorning = new DateTimeOffset(2026, 11, 2, 0, 30, 0, TimeSpan.FromHours(7));
        await TxAsync(db, user, vcb, "debit", 300_000, at);
        await TxAsync(db, user, vcb, "credit", 15_000_000, earlyMorning);
        await TxAsync(db, user, vcb, "credit", 100_000, at, relation: "refund");
        var original = await TxAsync(db, user, momo, "debit", 200_000, at, reversed: true);
        await TxAsync(db, user, momo, "credit", 200_000, at.AddDays(1), relation: "reversal", related: original);   // lệnh đảo ngày hôm sau
        await TxAsync(db, user, vcb, "transfer", 2_000_000, at, counter: momo);
        await TxAsync(db, user, vcb, "debit", 5_000_000, at, category: loan);
        await TxAsync(db, user, vcb, "credit", 2_000_000, at, category: loan);
        await TxAsync(db, user, momo, "transfer", 500_000, at, fundEntry: entryOut, fundDirection: "out");
        await TxAsync(db, user, momo, "transfer", 150_000, at, fundEntry: entryIn, fundDirection: "in");
        await TxAsync(db, user, vcb, "debit", 9_999_999, at, status: "draft");
        await TxAsync(db, user, vcb, "debit", 8_888_888, at, deleted: true);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO daily_summaries (id, user_id, summary_date, total_spent_cents, total_income_cents, total_refunded_cents, transaction_count,
                                         top_category_spent_cents, created_at, updated_at)
            VALUES ({Guid.NewGuid()}, {user}, {Day}, 0, 0, 0, 0, 0, now(), now())
            """);

        await db.Database.ExecuteSqlRawAsync(DailySummaryCashFlowBackfill.Sql);

        var expected = (await new ReportRepository(db).GetDailySpendAsync(user, Day, Day)).Single();
        db.ChangeTracker.Clear();
        var row = await db.DailySummaries.AsNoTracking().SingleAsync(s => s.UserId == user && s.SummaryDate == Day);
        (row.CashFlowNetCents, row.LoanOutflowCents, row.LoanInflowCents, row.GroupFundOutflowCents, row.GroupFundInflowCents, row.ReversalNetCents)
            .Should().Be((expected.CashNetCents, expected.LoanOutflowCents, expected.LoanInflowCents, expected.GroupFundOutflowCents, expected.GroupFundInflowCents,
                expected.ReversalNetCents));
        (row.LoanOutflowCents, row.LoanInflowCents, row.ReversalNetCents).Should().Be((5_000_000L, 2_000_000L, -200_000L));
        row.CashFlowNetCents.Should().Be(-300_000 + 15_000_000 + 100_000 - 200_000 - 5_000_000 + 2_000_000 - 500_000 + 150_000);
    }

    private static async Task<(Guid User, Guid Vcb, Guid Momo, Guid Loan, Guid EntryOut, Guid EntryIn)> SeedAsync(FinMateDbContext db)
    {
        var (user, vcb, momo, group, fund, entryOut, entryIn) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Cash Flow Backfill");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({vcb}, {user}, 'VCB', 'bank', now(), now()), ({momo}, {user}, 'MoMo', 'ewallet', now(), now())
            """);
        var loan = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM categories WHERE slug = 'loan' AND user_id IS NULL").FirstOrDefaultAsync();
        if (loan == Guid.Empty)
        {
            loan = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO categories (id, user_id, name, slug, created_at, updated_at) VALUES ({loan}, NULL, 'Cho vay / Mượn', 'loan', now(), now())");
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO groups (id, name, type, transaction_visibility, created_by_user_id, created_at, updated_at)
            VALUES ({group}, 'Phòng trọ', 'family', 'full', {user}, now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO group_funds (id, group_id, name, created_by_user_id, created_at, updated_at) VALUES ({fund}, {group}, 'Quỹ', {user}, now(), now())
            """);
        foreach (var (entry, kind) in new[] { (entryOut, "contribution"), (entryIn, "withdrawal") })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO group_fund_entries (id, fund_id, group_id, member_user_id, kind, amount_cents, transacted_at, origin, created_at, updated_at)
                VALUES ({entry}, {fund}, {group}, {user}, {kind}, 100000, now(), 'created', now(), now())
                """);
        }

        return (user, vcb, momo, loan, entryOut, entryIn);
    }

    private static async Task<Guid> TxAsync(
        FinMateDbContext db, Guid user, Guid account, string type, long amount, DateTimeOffset at, Guid? counter = null, Guid? category = null,
        string? relation = null, bool reversed = false, Guid? fundEntry = null, string? fundDirection = null, string status = "confirmed", bool deleted = false,
        Guid? related = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, counter_account_id, category_id, amount_cents, transaction_type, source, status,
                                      relation, related_transaction_id, is_reversed, group_fund_entry_id, group_fund_direction, transacted_at, created_at, updated_at, deleted_at)
            VALUES ({id}, {user}, {account}, {counter}, {category}, {amount}, {type}, 'manual', {status},
                    {relation}, {related}, {reversed}, {fundEntry}, {fundDirection}, {at.ToUniversalTime()}, now(), now(), {(deleted ? DateTimeOffset.UtcNow : (DateTimeOffset?)null)})
            """);
        return id;
    }
}
