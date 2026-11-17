using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Repositories;

/// <summary>Hai truy vấn của bộ nhớ món trên Postgres thật: món của một nhóm (đúng chủ sở hữu, chỉ món thường) và nhóm mà giao dịch đã nối.</summary>
public class ReceiptScanGroupQueryTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public ReceiptScanGroupQueryTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid User, Guid Account)> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Group Query Test");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at)
            VALUES ({account}, {user}, 'Ví', 'cash', now(), now())
            """);
        return (user, account);
    }

    private static async Task<Guid> SeedScanAsync(FinMateDbContext db, Guid user)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO receipt_scans (id, user_id, ocr_result, amount_cents, category_slug, created_at)
            VALUES ({id}, {user}, 'success', 16000, 'food', now())
            """);
        return id;
    }

    private static async Task<Guid> SeedTransactionAsync(FinMateDbContext db, Guid user, Guid account)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at)
            VALUES ({id}, {user}, {account}, 16000, 'debit', 'receipt', 'confirmed', now(), clock_timestamp(), now())
            """);
        return id;
    }

    private static ReceiptScanItem Item(int pos, string name, string? slug, string? source = "model", int? parent = null)
        => new() { Position = pos, Name = name, AmountCents = source == "discount" ? -1_000 : 5_000, CategorySlug = slug, CategorySource = source, ParentPosition = parent };

    [Fact]
    public async Task GetGroupItemsAsync_ReturnsOnlyOwnRegularItemsOfTheGroup()
    {
        await using var db = _fixture.NewContext();
        var (user, _) = await SeedUserAsync(db);
        var (other, _) = await SeedUserAsync(db);
        var repo = new ReceiptScanRepository(db);
        var scan = await SeedScanAsync(db, user);
        await repo.AddItemsAsync(scan,
        [
            Item(2, "Sữa", "food"),
            Item(0, "Gạo", "food"),
            Item(1, "Nước rửa chén", "household"),
            Item(3, "Giảm giá", "food", "discount"),
            Item(4, "Phụ thu", "food", "surcharge"),
            Item(5, "Món phụ", "food", "model", parent: 0),
            Item(6, "Món lạ", null),
        ]);

        var food = await repo.GetGroupItemsAsync(scan, user, "food");
        var unknown = await repo.GetGroupItemsAsync(scan, user, ReceiptScanLink.UnknownGroup);
        var asOther = await repo.GetGroupItemsAsync(scan, other, "food");

        food.Select(i => i.Name).Should().Equal("Gạo", "Sữa");  // theo thứ tự dòng, không có giảm giá / phụ thu / món phụ
        unknown.Select(i => i.Name).Should().Equal("Món lạ");
        asOther.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinkedGroupAsync_ReturnsScanAndGroup()
    {
        await using var db = _fixture.NewContext();
        var (user, account) = await SeedUserAsync(db);
        var (other, _) = await SeedUserAsync(db);
        var repo = new ReceiptScanRepository(db);
        var scan = await SeedScanAsync(db, user);
        var linked = await SeedTransactionAsync(db, user, account);
        var unlinked = await SeedTransactionAsync(db, user, account);
        await repo.LinkToTransactionAsync(scan, user, linked, "food");

        (await repo.GetLinkedGroupAsync(linked, user)).Should().Be((scan, "food"));
        (await repo.GetLinkedGroupAsync(unlinked, user)).Should().BeNull();
        (await repo.GetLinkedGroupAsync(linked, other)).Should().BeNull();
    }
}
