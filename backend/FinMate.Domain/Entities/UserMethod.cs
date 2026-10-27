using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Phương pháp người dùng ĐANG dùng (hoặc đã dùng) và các tham số họ tự chỉnh. Khác
/// <see cref="MethodRecommendation"/>: "được gợi ý" và "đang dùng" là hai chuyện — người dùng có thể
/// bỏ qua gợi ý và tự chọn. Mỗi người chỉ có một phương pháp chính và một bổ trợ đang hoạt động.
/// </summary>
public class UserMethod
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MethodId { get; set; }

    /// <summary>Phiên bản của phương pháp lúc người dùng chọn.</summary>
    public int MethodVersion { get; set; }

    public UserMethodRole Role { get; set; } = UserMethodRole.Primary;

    /// <summary>JSON object CHỈ chứa phần người dùng ghi đè (T1, T2…). Thiếu khóa nào thì dùng mặc định hiện hành của phương pháp.</summary>
    public string Params { get; set; } = "{}";

    public Guid? RecommendationId { get; set; }
    public MethodChoiceSource ChosenVia { get; set; }
    public UserMethodStatus Status { get; set; } = UserMethodStatus.Active;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
