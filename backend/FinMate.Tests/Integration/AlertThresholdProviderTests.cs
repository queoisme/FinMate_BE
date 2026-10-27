using FinMate.Application.Budgets;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Ngưỡng T1/T2 hiệu lực của một người dùng, đọc từ <c>user_methods</c> + <c>finance_methods</c> trên Postgres thật với dữ liệu
/// do seeder tạo. Chỉ phương pháp ĐANG DÙNG được tính, cách chính thắng cách bổ trợ, và không có gì để đọc thì quay về 70/90.
/// </summary>
public class AlertThresholdProviderTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public AlertThresholdProviderTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<FinMateDbContext> NewSeededDbAsync()
    {
        var db = _fixture.NewContext(_fixture.ConnectionStringFor($"thr_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        await FinanceMethodSeeder.SeedAsync(db);
        return db;
    }

    private static async Task<Guid> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var email = $"{user:N}@finmate.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, mission_reminders_enabled, created_at, updated_at)
            VALUES ({user}, {email}, 'Threshold Test', 'user', true, true, true, now(), now())
            """);
        return user;
    }

    private static async Task UseMethodAsync(FinMateDbContext db, Guid user, string methodCode, string role, string status, string paramsJson)
    {
        var methodId = await db.FinanceMethods.Where(m => m.Code == methodCode).Select(m => m.Id).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_methods (user_id, method_id, method_version, role, params, chosen_via, status, started_at, ended_at, created_at, updated_at)
            VALUES ({user}, {methodId}, 1, {role}, CAST({paramsJson} AS jsonb), 'manual', {status}, now() - interval '1 day',
                    CASE WHEN {status} = 'ended' THEN now() END, now(), now())
            """);
    }

    [Fact]
    public async Task NoActiveMethod_ReturnsTheDefaults()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(AlertThresholds.Default);
    }

    [Fact]
    public async Task ActiveMethodWithoutOverrides_ReturnsTheMethodDefaults()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "category_budget", "primary", "active", "{}");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(new AlertThresholds(70, 90));
    }

    [Fact]
    public async Task ThePersonalOverride_Wins()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "category_budget", "primary", "active", """{"t1_pct":55,"t2_pct":85}""");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(new AlertThresholds(55, 85));
    }

    [Fact]
    public async Task AnOverrideThatBreaksTheOrder_IsIgnored()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "category_budget", "primary", "active", """{"t1_pct":95,"t2_pct":50}""");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(new AlertThresholds(70, 90));
    }

    [Fact]
    public async Task OnlyActiveMethodsCount()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "category_budget", "primary", "ended", """{"t1_pct":10,"t2_pct":20}""");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(AlertThresholds.Default);
    }

    [Fact]
    public async Task ThePrimaryMethodBeatsTheSupportingOne()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "envelope_budget", "supporting", "active", """{"t1_pct":30,"t2_pct":50}""");
        await UseMethodAsync(db, user, "available_to_spend", "primary", "active", """{"t1_pct":60,"t2_pct":80}""");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(new AlertThresholds(60, 80));
    }

    [Fact]
    public async Task AMethodWithoutThresholds_FallsThroughToTheNextOrTheDefaults()
    {
        await using var db = await NewSeededDbAsync();
        var user = await SeedUserAsync(db);
        await UseMethodAsync(db, user, "minimal_tracking", "primary", "active", "{}");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(AlertThresholds.Default);

        await UseMethodAsync(db, user, "envelope_budget", "supporting", "active", """{"t1_pct":40,"t2_pct":75}""");

        (await new AlertThresholdProvider(db).GetAsync(user)).Should().Be(new AlertThresholds(40, 75));
    }

    [Fact]
    public async Task OtherPeoplesMethodsAreNeverRead()
    {
        await using var db = await NewSeededDbAsync();
        var me = await SeedUserAsync(db);
        var someoneElse = await SeedUserAsync(db);
        await UseMethodAsync(db, someoneElse, "category_budget", "primary", "active", """{"t1_pct":20,"t2_pct":30}""");

        (await new AlertThresholdProvider(db).GetAsync(me)).Should().Be(AlertThresholds.Default);
    }
}
