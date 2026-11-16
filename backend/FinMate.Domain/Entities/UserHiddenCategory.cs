namespace FinMate.Domain.Entities;

/// <summary>Người dùng ẩn một danh mục HỆ THỐNG khỏi danh sách của riêng mình. Danh mục dùng chung không đổi; giao dịch cũ giữ nguyên danh mục.</summary>
public class UserHiddenCategory
{
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
