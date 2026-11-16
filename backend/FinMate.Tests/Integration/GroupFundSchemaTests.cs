using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ quỹ nhóm (Phase 85) trên Postgres thật: hình dạng transfer gắn quỹ (BR-FUND-08, D1), các CHECK của dòng quỹ (D3, D10, D24),
/// quỹ lưu trữ phải hết tiền (BR-FUND-03), xoá cứng tài khoản ẩn danh dòng quỹ (BR-FUND-14). Gieo bằng SQL thô để kiểm chính database.
/// </summary>
public class GroupFundSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private readonly ReceiptScanDbFixture _fixture;

    public GroupFundSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seed(Guid User, Guid Account, Guid OtherAccount, Guid Group, Guid Fund, Guid Category);

    private static async Task<Seed> SeedAsync(FinMateDbContext db)
    {
        var (user, account, other, group, fund, category) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Fund Schema Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'VCB', 'bank', now(), now()), ({other}, {user}, 'Tiền mặt', 'cash', now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO groups (id, name, type, transaction_visibility, created_by_user_id, created_at, updated_at)
            VALUES ({group}, 'Phòng trọ', 'family', 'full', {user}, now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO group_funds (id, group_id, name, created_by_user_id, created_at, updated_at) VALUES ({fund}, {group}, 'Tiền điện nước', {user}, now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO categories (id, user_id, name, slug, created_at, updated_at) VALUES ({category}, NULL, {category.ToString("N")}, {category.ToString("N")}, now(), now())
            """);
        return new Seed(user, account, other, group, fund, category);
    }

    private static async Task<Guid> InsertEntryAsync(
        FinMateDbContext db, Seed s, string kind = "contribution", long amount = 500_000, Guid? category = null, string? note = null,
        string? merchant = null, string origin = "created", Guid? originCategory = null)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO group_fund_entries (id, fund_id, group_id, member_user_id, kind, amount_cents, transacted_at, category_id, note, merchant_name,
                                            origin, origin_category_id, created_at, updated_at)
            VALUES ({id}, {s.Fund}, {s.Group}, {s.User}, {kind}, {amount}, now(), {category}, {note}, {merchant}, {origin}, {originCategory}, now(), now())
            """);
        return id;
    }

    private static Task InsertTransactionAsync(
        FinMateDbContext db, Seed s, string type, Guid? counter = null, Guid? entry = null, string? direction = null, Guid? category = null,
        string status = "confirmed", Guid? id = null)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, counter_account_id, category_id, amount_cents, transaction_type, source, status,
                                      transacted_at, created_at, updated_at, group_fund_entry_id, group_fund_direction)
            VALUES ({id ?? Guid.NewGuid()}, {s.User}, {s.Account}, {counter}, {category}, 500000, {type}, 'manual', {status}, now(), now(), now(), {entry}, {direction})
            """);

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint, string state = CheckViolation)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(state);
        thrown.Which.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task OrdinaryTransfer_StillNeedsACounterAccount()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);

        await InsertTransactionAsync(db, s, "transfer", counter: s.OtherAccount);
        await ShouldViolateAsync(() => InsertTransactionAsync(db, s, "transfer"), "chk_transactions_transfer_shape");
    }

    [Theory]
    [InlineData("out")]
    [InlineData("in")]
    public async Task FundLinkedTransfer_WithoutCounterAccount_IsAccepted(string direction)
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var entry = await InsertEntryAsync(db, s, direction == "out" ? "contribution" : "withdrawal");

        await InsertTransactionAsync(db, s, "transfer", entry: entry, direction: direction);
    }

    [Fact]
    public async Task FundLink_OnADebit_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var entry = await InsertEntryAsync(db, s);

        await ShouldViolateAsync(() => InsertTransactionAsync(db, s, "debit", entry: entry, direction: "out"), "chk_transactions_group_fund_link");
    }

    [Fact]
    public async Task FundLink_WithACounterAccount_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var entry = await InsertEntryAsync(db, s);

        await ShouldViolateAsync(
            () => InsertTransactionAsync(db, s, "transfer", counter: s.OtherAccount, entry: entry, direction: "out"), "chk_transactions_group_fund_link");
    }

    [Fact]
    public async Task FundLink_WithoutDirection_OrDraft_IsRejected()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var first = await InsertEntryAsync(db, s);
        var second = await InsertEntryAsync(db, s);

        await ShouldViolateAsync(() => InsertTransactionAsync(db, s, "transfer", entry: first), "chk_transactions_group_fund_link");
        await ShouldViolateAsync(() => InsertTransactionAsync(db, s, "transfer", entry: second, direction: "out", status: "draft"), "chk_transactions_group_fund_link");
    }

    [Fact]
    public async Task OneEntry_HasAtMostOnePersonalTransaction()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var entry = await InsertEntryAsync(db, s);
        await InsertTransactionAsync(db, s, "transfer", entry: entry, direction: "out");

        await ShouldViolateAsync(
            () => InsertTransactionAsync(db, s, "transfer", entry: entry, direction: "out"), "uq_transactions_group_fund_entry_id", UniqueViolation);
    }

    [Theory]
    [InlineData("contribution", 0L, null)]
    [InlineData("withdrawal", -1L, null)]
    [InlineData("expense", -800_000L, null)]
    [InlineData("adjustment", 0L, "đếm lại")]
    [InlineData("adjustment", 100_000L, null)]
    public async Task EntryAmount_FollowsTheKind(string kind, long amount, string? note)
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);

        await ShouldViolateAsync(
            () => InsertEntryAsync(db, s, kind, amount, category: kind == "expense" ? s.Category : null, note: note), "chk_group_fund_entries_amount");
    }

    [Fact]
    public async Task Adjustment_MayBeNegative_WithANote()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);

        await InsertEntryAsync(db, s, "adjustment", -50_000, note: "Đếm lại tiền mặt");
    }

    [Fact]
    public async Task Expense_NeedsACategory_AndOnlyExpensesCarryOne()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);

        await InsertEntryAsync(db, s, "expense", 800_000, category: s.Category, merchant: "EVN");
        await ShouldViolateAsync(() => InsertEntryAsync(db, s, "expense", 800_000), "chk_group_fund_entries_expense_category");
        await ShouldViolateAsync(() => InsertEntryAsync(db, s, "contribution", category: s.Category), "chk_group_fund_entries_expense_fields");
        await ShouldViolateAsync(() => InsertEntryAsync(db, s, "contribution", merchant: "EVN"), "chk_group_fund_entries_expense_fields");
    }

    [Fact]
    public async Task OnlyExpenses_CanBeConverted_AndOnlyConvertedKeepAnOriginCategory()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);

        await InsertEntryAsync(db, s, "expense", 800_000, category: s.Category, origin: "converted", originCategory: s.Category);
        await ShouldViolateAsync(() => InsertEntryAsync(db, s, "contribution", origin: "converted"), "chk_group_fund_entries_origin_kind");
        await ShouldViolateAsync(
            () => InsertEntryAsync(db, s, "expense", 800_000, category: s.Category, originCategory: s.Category), "chk_group_fund_entries_origin_category");
    }

    [Fact]
    public async Task ArchivedFund_MustBeSettled()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE group_funds SET balance_cents = -200000 WHERE id = {s.Fund}");

        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE group_funds SET archived_at = now() WHERE id = {s.Fund}"), "chk_group_funds_archived_settled");

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE group_funds SET balance_cents = 0, archived_at = now() WHERE id = {s.Fund}");
    }

    [Fact]
    public async Task HardDeletingTheMember_AnonymisesTheEntry_AndKeepsIt()
    {
        await using var db = _fixture.NewContext();
        var s = await SeedAsync(db);
        var entry = await InsertEntryAsync(db, s);

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {s.User}");

        var rows = await db.Database.SqlQuery<Guid?>($"SELECT member_user_id AS \"Value\" FROM group_fund_entries WHERE id = {entry}").ToListAsync();
        rows.Should().ContainSingle().Which.Should().BeNull("BR-FUND-14: dòng quỹ ở lại, chỉ mất liên kết người");
    }
}
