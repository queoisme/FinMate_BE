using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Tests.Integration;

/// <summary>
/// Chèn một user bằng SQL thô cho các test migration. Các test này đưa DB về một migration CŨ rồi mới chèn,
/// nên câu INSERT phải khớp schema ở thời điểm đó: trước migration <c>RemoveGamification</c>, cột
/// <c>mission_reminders_enabled</c> còn tồn tại và NOT NULL, sau đó thì không còn.
/// </summary>
internal static class RawUserSeed
{
    public static async Task InsertAsync(FinMateDbContext db, Guid user, string email, string displayName)
    {
        var hasMissionColumn = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'users' AND column_name = 'mission_reminders_enabled'
            ) AS "Value"
            """).SingleAsync();

        if (hasMissionColumn)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, mission_reminders_enabled, created_at, updated_at)
                VALUES ({user}, {email}, {displayName}, 'user', true, true, true, now(), now())
                """);
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, display_name, role, push_enabled, budget_alerts_enabled, created_at, updated_at)
            VALUES ({user}, {email}, {displayName}, 'user', true, true, now(), now())
            """);
    }
}
