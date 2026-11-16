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

    private const string Empty = "{}";

    [Fact]
    public async Task TransferOutcomes_AreCountedByKind()
    {
        await using var db = _fixture.NewContext();
        var user = Guid.NewGuid();
        var account = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Transfer stats");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_accounts (id, user_id, account_name, account_type, created_at, updated_at) VALUES ({account}, {user}, 'VCB', 'bank', now(), now())
            """);
        async Task<Guid> LogAsync(string? hint, string? kind)
        {
            var log = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_logs (id, user_id, package_name, content_hash, status, received_at, created_at)
                VALUES ({log}, {user}, 'com.VCB', {log.ToString("N")}, 'processed', now(), now())
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ai_results (id, notification_log_id, pipeline_result, is_potential_duplicate, relation_hint, transfer_kind, created_at)
                VALUES ({Guid.NewGuid()}, {log}, 'financial', false, {hint}, {kind}, now())
                """);
            return log;
        }

        await LogAsync("transfer", "atm_withdrawal");
        await LogAsync("transfer", "atm_withdrawal");
        var dismissedLog = await LogAsync("transfer", "wallet_topup");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, user_id, financial_account_id, notification_log_id, amount_cents, transaction_type, source, status, transacted_at, created_at, updated_at, transfer_dismissed_at)
            VALUES ({Guid.NewGuid()}, {user}, {account}, {dismissedLog}, 500000, 'debit', 'notification', 'draft', now(), now(), now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transaction_merge_snapshots (id, user_id, survivor_id, kind, transfer_kind, before, created_at)
            VALUES ({Guid.NewGuid()}, {user}, {Guid.NewGuid()}, 'transfer_convert', 'atm_withdrawal', CAST({Empty} AS jsonb), now()),
                   ({Guid.NewGuid()}, {user}, {Guid.NewGuid()}, 'transfer_pair', NULL, CAST({Empty} AS jsonb), now())
            """);

        var stats = (await new AiStatsRepository(db).GetProductionStatsAsync(Now.AddDays(-30), DateTimeOffset.UtcNow.AddMinutes(1))).Transfers!;

        stats.ByKind["atm_withdrawal"].Should().Be(new FinMate.Application.Common.Interfaces.TransferKindCounts(2, 1, 0));
        stats.ByKind["wallet_topup"].Should().Be(new FinMate.Application.Common.Interfaces.TransferKindCounts(1, 0, 1));
        stats.ByKind["unhinted"].Should().Be(new FinMate.Application.Common.Interfaces.TransferKindCounts(0, 1, 0));
    }
}

