using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Lược đồ cho "cách quản lý tài chính" + khảo sát + mục tiêu theo mẫu, trên Postgres thật: migration
/// backfill đúng và đi xuống/lên lại được, các ràng buộc CHECK/UNIQUE/FK chặn đúng dữ liệu sai, và xóa
/// tài khoản kéo theo dữ liệu người dùng (UserHardDeleter chỉ xóa dòng users, phần còn lại dựa vào cascade).
/// Dùng chung container với <see cref="ReceiptScanDbFixture"/>; gieo dữ liệu bằng SQL thô để không phụ thuộc mô hình EF.
/// </summary>
public class FinanceMethodSchemaTests : IClassFixture<ReceiptScanDbFixture>
{
    private const string MigrationBefore = "20261014051505_AddTrainingConsent";
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private readonly ReceiptScanDbFixture _fixture;

    public FinanceMethodSchemaTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- gieo dữ liệu bằng SQL thô ----

    private static async Task<Guid> SeedUserAsync(FinMateDbContext db)
    {
        var user = Guid.NewGuid();
        var email = $"{user:N}@finmate.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, mission_reminders_enabled, created_at, updated_at)
            VALUES ({user}, {email}, 'Schema Test', 'user', true, true, true, now(), now())
            """);
        return user;
    }

    private static async Task<Guid> SeedMethodAsync(FinMateDbContext db)
    {
        var id = Guid.NewGuid();
        var code = $"m_{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance_methods (id, code, name, summary, role, goal_mode, why_text, created_at, updated_at)
            VALUES ({id}, {code}, 'Method', 'Summary', 'primary', 'track_only', 'Why', now(), now())
            """);
        return id;
    }

    private static Task InsertUserMethodAsync(
        FinMateDbContext db, Guid user, Guid method, string role, string status, DateTimeOffset? endedAt)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_methods (user_id, method_id, method_version, role, chosen_via, status, started_at, ended_at, created_at, updated_at)
            VALUES ({user}, {method}, 1, {role}, 'manual', {status}, now() - interval '1 day', {endedAt}, now(), now())
            """);

    private static Task InsertRecommendationAsync(
        FinMateDbContext db, Guid user, Guid primary, Guid? support, string source, string? promptVersion, string outcome, bool decided)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO method_recommendations (user_id, primary_method_id, support_method_id, source, prompt_version, outcome, decided_at, created_at)
            VALUES ({user}, {primary}, {support}, {source}, {promptVersion}, {outcome}, {(decided ? DateTimeOffset.UtcNow : (DateTimeOffset?)null)}, now())
            """);

    private static async Task<long> CountAsync(FinMateDbContext db, FormattableString sql)
        => await db.Database.SqlQuery<long>(sql).SingleAsync();

    private static async Task ShouldViolateAsync(Func<Task> action, string sqlState)
    {
        var thrown = await action.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(sqlState);
    }

    // ---- migration ----

    [Fact]
    public async Task Migration_BackfillsExistingUsersAsCompleted_AndNewUsersStartPending()
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        var existing = await SeedUserAsync(db);   // có TRƯỚC khi cột onboarding tồn tại
        await migrator.MigrateAsync();
        var created = await SeedUserAsync(db);    // tạo SAU migration, không khai cột nào

        FormattableString statusSql = $"""SELECT onboarding_status AS "Value" FROM users WHERE id = {existing}""";
        (await db.Database.SqlQuery<string>(statusSql).SingleAsync()).Should().Be("completed");
        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM users WHERE id = {existing} AND onboarding_completed_at IS NOT NULL"))
            .Should().Be(1);

        (await db.Database.SqlQuery<string>($"""SELECT onboarding_status AS "Value" FROM users WHERE id = {created}""").SingleAsync())
            .Should().Be("pending");
        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM users WHERE id = {created} AND onboarding_completed_at IS NULL"))
            .Should().Be(1);

        // Down rồi Up lại: cột biến mất rồi trở lại, và backfill chạy lại cho tài khoản có sẵn.
        await migrator.MigrateAsync(MigrationBefore);
        await migrator.MigrateAsync();
        (await db.Database.SqlQuery<string>(statusSql).SingleAsync()).Should().Be("completed");
    }

    [Fact]
    public async Task Migration_RenamesAlertFlagsWithoutLosingTheTimestamps_AndRestoresThemOnDown()
    {
        const string before = "20261026202125_AddFinanceMethodsAndOnboarding";
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"mig_{Guid.NewGuid():N}"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(before);

        var user = await SeedUserAsync(db);
        var budget = Guid.NewGuid();
        var period = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO budgets (id, user_id, limit_cents, period_type, created_at, updated_at)
            VALUES ({budget}, {user}, 1000000, 'monthly', now(), now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO budget_periods (id, budget_id, period_start, period_end, limit_cents, spent_cents, alert_70_sent_at, alert_90_sent_at, alert_100_sent_at, created_at, updated_at)
            VALUES ({period}, {budget}, now() - interval '1 day', now() + interval '29 days', 1000000, 950000, now(), now(), NULL, now(), now())
            """);

        await migrator.MigrateAsync();

        // Đổi tên, không phải xóa rồi tạo lại: mốc đã gửi (và mốc CHƯA gửi) giữ nguyên, nên không ai nhận lại cảnh báo đã nhận.
        (await CountAsync(db, $"""
            SELECT count(*) AS "Value" FROM budget_periods
            WHERE id = {period} AND alert_t1_sent_at IS NOT NULL AND alert_t2_sent_at IS NOT NULL AND alert_over_sent_at IS NULL
            """)).Should().Be(1);

        await migrator.MigrateAsync(before);

        (await CountAsync(db, $"""
            SELECT count(*) AS "Value" FROM budget_periods
            WHERE id = {period} AND alert_70_sent_at IS NOT NULL AND alert_90_sent_at IS NOT NULL AND alert_100_sent_at IS NULL
            """)).Should().Be(1);
    }

    [Fact]
    public async Task Users_RejectASurveyAiConsentVersionWithoutATimestamp_AndViceVersa()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);

        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET survey_ai_consent_version = 1 WHERE id = {user}"),
            CheckViolation);
        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET survey_ai_consent_at = now() WHERE id = {user}"),
            CheckViolation);

        // Đồng ý và rút lại đều đặt/xóa CẢ HAI cột cùng lúc.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET survey_ai_consent_version = 1, survey_ai_consent_at = now() WHERE id = {user}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET survey_ai_consent_version = NULL, survey_ai_consent_at = NULL WHERE id = {user}");
    }

    // ---- user_methods ----

    [Fact]
    public async Task UserMethods_AllowOneActivePrimaryAndOneActiveSupportingPerUser()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var method = await SeedMethodAsync(db);

        await InsertUserMethodAsync(db, user, method, "primary", "active", null);
        await InsertUserMethodAsync(db, user, method, "supporting", "active", null);

        await ShouldViolateAsync(
            () => InsertUserMethodAsync(db, user, method, "primary", "active", null), UniqueViolation);

        // Lịch sử không bị giới hạn: nhiều dòng đã kết thúc cùng vai trò vẫn được.
        await InsertUserMethodAsync(db, user, method, "primary", "ended", DateTimeOffset.UtcNow);
        await InsertUserMethodAsync(db, user, method, "primary", "ended", DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task UserMethods_RejectStatusAndEndedAtThatDisagree()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var method = await SeedMethodAsync(db);

        await ShouldViolateAsync(
            () => InsertUserMethodAsync(db, user, method, "primary", "ended", null), CheckViolation);
        await ShouldViolateAsync(
            () => InsertUserMethodAsync(db, user, method, "primary", "active", DateTimeOffset.UtcNow), CheckViolation);
    }

    // ---- method_recommendations ----

    [Fact]
    public async Task Recommendations_RejectSameSupportAsPrimary_BadDecisionState_AndAiWithoutPromptVersion()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var primary = await SeedMethodAsync(db);

        await ShouldViolateAsync(
            () => InsertRecommendationAsync(db, user, primary, primary, "ai", "v1", "shown", false), CheckViolation);
        await ShouldViolateAsync(
            () => InsertRecommendationAsync(db, user, primary, null, "ai", "v1", "shown", true), CheckViolation);
        await ShouldViolateAsync(
            () => InsertRecommendationAsync(db, user, primary, null, "ai", "v1", "accepted", false), CheckViolation);
        await ShouldViolateAsync(
            () => InsertRecommendationAsync(db, user, primary, null, "ai", null, "shown", false), CheckViolation);

        // Rule fallback không gọi AI nên không cần prompt_version.
        await InsertRecommendationAsync(db, user, primary, null, "rule_fallback", null, "shown", false);
    }

    // ---- danh mục: JSON sai kiểu, không xóa cứng ----

    [Fact]
    public async Task FinanceMethods_RejectJsonOfTheWrongShape_AndCannotBeDeletedWhileReferenced()
    {
        await using var db = _fixture.NewContext();
        var code = $"bad_{Guid.NewGuid():N}";
        const string objectWhereAnArrayIsRequired = "{}";   // param_defs phải là mảng

        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO finance_methods (code, name, summary, role, goal_mode, why_text, param_defs, created_at, updated_at)
                VALUES ({code}, 'Bad', 'Bad', 'primary', 'track_only', 'Bad', CAST({objectWhereAnArrayIsRequired} AS jsonb), now(), now())
                """),
            CheckViolation);

        var user = await SeedUserAsync(db);
        var method = await SeedMethodAsync(db);
        await InsertRecommendationAsync(db, user, method, null, "rule_fallback", null, "shown", false);

        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM finance_methods WHERE id = {method}"),
            ForeignKeyViolation);
    }

    // ---- xóa tài khoản và mục tiêu ----

    [Fact]
    public async Task DeletingUser_CascadesSurveyAnswers_Recommendations_AndUserMethods()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var method = await SeedMethodAsync(db);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_survey_answers (user_id, question_code, option_code, answered_at, updated_at)
            VALUES ({user}, 'q1', 'A', now(), now())
            """);
        await InsertRecommendationAsync(db, user, method, null, "rule_fallback", null, "shown", false);
        await InsertUserMethodAsync(db, user, method, "primary", "active", null);

        // Mỗi người chỉ một câu trả lời cho mỗi câu hỏi.
        await ShouldViolateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO user_survey_answers (user_id, question_code, option_code, answered_at, updated_at)
                VALUES ({user}, 'q1', 'B', now(), now())
                """),
            UniqueViolation);

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {user}");

        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM user_survey_answers WHERE user_id = {user}")).Should().Be(0);
        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM method_recommendations WHERE user_id = {user}")).Should().Be(0);
        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM user_methods WHERE user_id = {user}")).Should().Be(0);
    }

    [Fact]
    public async Task SavingGoal_KeepsLivingWhenItsTemplateOrMethodRowDisappears()
    {
        await using var db = _fixture.NewContext();
        var user = await SeedUserAsync(db);
        var method = await SeedMethodAsync(db);
        await InsertUserMethodAsync(db, user, method, "primary", "active", null);
        var userMethod = await db.Database
            .SqlQuery<Guid>($"""SELECT id AS "Value" FROM user_methods WHERE user_id = {user}""").SingleAsync();

        var goal = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO saving_goals (id, user_id, name, target_cents, user_method_id, created_at, updated_at)
            VALUES ({goal}, {user}, 'Laptop', 80000000, {userMethod}, now(), now())
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_methods WHERE id = {userMethod}");

        (await CountAsync(db, $"SELECT count(*) AS \"Value\" FROM saving_goals WHERE id = {goal} AND user_method_id IS NULL"))
            .Should().Be(1);
    }
}
