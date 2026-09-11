using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Bộ mascot item miễn phí. Phase 7 chỉ seed free tier — cột <c>is_premium</c> tồn tại để
/// dành chỗ cho tier trả phí sau này, chưa có item nào dùng tới.
/// </summary>
public static class MascotItemSeeder
{
    private static readonly (string Code, string Name, MascotItemType Type, MascotUnlockType Unlock, int? Level, string? MissionCode)[] Items =
    {
        ("outfit_basic", "Áo thun cơ bản", MascotItemType.Outfit, MascotUnlockType.Default, null, null),
        ("background_home", "Nền phòng khách", MascotItemType.Background, MascotUnlockType.Default, null, null),
        ("hat_cap", "Mũ lưỡi trai", MascotItemType.Hat, MascotUnlockType.Level, 2, null),
        ("accessory_glasses", "Kính râm", MascotItemType.Accessory, MascotUnlockType.Level, 3, null),
        ("outfit_office", "Sơ mi công sở", MascotItemType.Outfit, MascotUnlockType.Level, 5, null),
        ("background_beach", "Nền bãi biển", MascotItemType.Background, MascotUnlockType.Level, 8, null),
        ("hat_piggy", "Mũ heo đất", MascotItemType.Hat, MascotUnlockType.Mission, null, "first_goal_contribution"),
        ("accessory_medal", "Huy chương chuỗi 7 ngày", MascotItemType.Accessory, MascotUnlockType.Mission, null, "streak_7"),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existing = await context.MascotItems.Select(i => i.Code).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = false;

        foreach (var (code, name, type, unlock, level, missionCode) in Items)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            context.MascotItems.Add(new MascotItem
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                ItemType = type,
                UnlockType = unlock,
                UnlockLevel = level,
                UnlockMissionCode = missionCode,
                IsPremium = false,
                CreatedAt = now,
            });
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
