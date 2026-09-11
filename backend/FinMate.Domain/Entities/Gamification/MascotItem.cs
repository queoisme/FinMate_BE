using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities.Gamification;

public class MascotItem
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MascotItemType ItemType { get; set; }

    public MascotUnlockType UnlockType { get; set; }

    /// <summary>Level cần đạt khi <see cref="UnlockType"/> là Level.</summary>
    public int? UnlockLevel { get; set; }

    /// <summary>Mã mission cần hoàn thành khi <see cref="UnlockType"/> là Mission.</summary>
    public string? UnlockMissionCode { get; set; }

    /// <summary>Để dành chỗ cho tier trả phí — Phase 7 chỉ seed item miễn phí.</summary>
    public bool IsPremium { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
