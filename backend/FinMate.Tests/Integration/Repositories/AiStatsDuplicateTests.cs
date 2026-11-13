using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Repositories;

/// <summary>
/// Phase 82 trên <c>/admin/ai-stats</c>: số nghi trùng, đã bác, đã gộp theo cặp nguồn và số thông báo bị bỏ vì là cùng một sự kiện. Chỉ số đếm,
/// không dữ liệu theo user. Container riêng cho lớp này nên con số là tuyệt đối.
/// </summary>
public class AiStatsDuplicateTests : IClassFixture<ReceiptScanDbFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly ReceiptScanDbFixture _fixture;

    public AiStatsDuplicateTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> InsertAsync(
        FinMateDbContext db, Guid user, Guid account, string source, Guid? duplicateOf = null, string? status = null, Guid? mergedInto = null,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        DateTimeOffset? deletedAt = mergedInto is null ? null : Now;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at,
                                      duplicate_of_transaction_id, duplicate_status, merged_into_transaction_id, deleted_at)
            VALUES ({id}, {user}, {account}, 50000, 'debit', {source}, 'draft', now(), {createdAt ?? Now}, now(), {duplicateOf}, {status}, {mergedInto}, {deletedAt})
            """);
        return id;
    }

    [Fact]
    public async Task CountsByPairWithoutAnythingPerUser()
    {
        await using var db = _fixture.NewContext();
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Stats");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at) VALUES ({account}, {user}, 'Ví', 'cash', now(), now())
            """);

        var chat = await InsertAsync(db, user, account, "manual");
        await InsertAsync(db, user, account, "notification", chat, "suspected");
        await InsertAsync(db, user, account, "notification", chat, "dismissed");
        var bank = await InsertAsync(db, user, account, "notification");
        await InsertAsync(db, user, account, "notification", bank, "suspected");
        var survivor = await InsertAsync(db, user, account, "manual");
        await InsertAsync(db, user, account, "notification", mergedInto: survivor);
        await InsertAsync(db, user, account, "notification", chat, "suspected", createdAt: Now.AddDays(-60));   // ngoài kỳ
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
            VALUES ({Guid.NewGuid()}, {user}, 'com.VCB', 'h1', 'duplicate', now(), now())
            """);

        var stats = await new AiStatsRepository(db).GetProductionStatsAsync(Now.AddDays(-30), Now.AddMinutes(1));

        var duplicates = stats.Duplicates!;
        duplicates.SameEventDropped.Should().Be(1);
        duplicates.ByPair.Keys.Should().BeEquivalentTo(["manual+notification", "notification+notification"]);
        duplicates.ByPair["manual+notification"].Should().Be(new FinMate.Application.Common.Interfaces.DuplicatePairCounts(1, 1, 1));
        duplicates.ByPair["notification+notification"].Should().Be(new FinMate.Application.Common.Interfaces.DuplicatePairCounts(1, 0, 0));
    }
}
