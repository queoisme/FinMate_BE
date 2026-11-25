using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>Gợi ý trả nợ ở <c>ai_results</c> (Phase 90, Task 2): lưu dạng snake_case kèm độ mạnh; độ mạnh chỉ đi cùng <c>debt_repayment</c>.</summary>
public class AiResultDebtHintSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public AiResultDebtHintSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DebtRepaymentHint_IsStoredAsSnakeCase_WithItsStrength()
    {
        await using var db = _fixture.NewContext();
        var user = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Hint Schema");
        var log = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, received_at, status, created_at)
            VALUES ({log}, {user}, 'com.VCB', {log.ToString("N")}, now(), 'processed', now())
            """);
        db.AiResults.Add(new AiResult
        {
            Id = Guid.NewGuid(), NotificationLogId = log, PipelineResult = PipelineResult.Financial, RelationHint = NotificationRelationHint.DebtRepayment,
            DebtHintStrength = DebtHintStrength.Weak, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        (await db.Database.SqlQuery<string>($"SELECT relation_hint AS \"Value\" FROM ai_results WHERE notification_log_id = {log}").SingleAsync())
            .Should().Be("debt_repayment");
        db.ChangeTracker.Clear();
        var read = await db.AiResults.AsNoTracking().SingleAsync(r => r.NotificationLogId == log);
        read.RelationHint.Should().Be(NotificationRelationHint.DebtRepayment);
        read.DebtHintStrength.Should().Be(DebtHintStrength.Weak);

        var thrown = await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ai_results SET relation_hint = 'refund' WHERE notification_log_id = {log}"))
            .Should().ThrowAsync<PostgresException>();
        thrown.Which.ConstraintName.Should().Be("chk_ai_results_debt_hint_strength");
    }

    [Fact]
    public async Task OlderHints_StillRoundTrip()
    {
        await using var db = _fixture.NewContext();
        var user = Guid.NewGuid();
        await RawUserSeed.InsertAsync(db, user, $"{user:N}@finmate.local", "Hint Schema");
        var log = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_logs (id, user_id, package_name, content_hash, received_at, status, created_at)
            VALUES ({log}, {user}, 'com.VCB', {log.ToString("N")}, now(), 'processed', now())
            """);
        db.AiResults.Add(new AiResult
        {
            Id = Guid.NewGuid(), NotificationLogId = log, PipelineResult = PipelineResult.Financial, RelationHint = NotificationRelationHint.Transfer,
            TransferKind = TransferKind.AtmWithdrawal, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        (await db.Database.SqlQuery<string>($"SELECT relation_hint AS \"Value\" FROM ai_results WHERE notification_log_id = {log}").SingleAsync())
            .Should().Be("transfer", "đổi sang snake_case tường minh không được đổi giá trị cũ");
    }
}
