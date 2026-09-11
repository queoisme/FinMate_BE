using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities.Gamification;

public class UserMascotItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MascotItemId { get; set; }

    /// <summary>
    /// Nhân bản từ <see cref="MascotItem.ItemType"/>: partial unique index của Postgres không
    /// tham chiếu được bảng join, nên muốn ràng buộc "mỗi loại chỉ mặc 1 món" ở tầng DB thì
    /// cột này phải nằm ngay đây.
    /// </summary>
    public MascotItemType ItemType { get; set; }

    public DateTimeOffset UnlockedAt { get; set; }
    public bool IsEquipped { get; set; }

    public MascotItem? MascotItem { get; set; }
}
