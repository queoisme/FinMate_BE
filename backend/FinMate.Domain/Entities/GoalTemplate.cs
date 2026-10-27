namespace FinMate.Domain.Entities;

/// <summary>Mẫu mục tiêu tiết kiệm có sẵn để gợi ý (vd quỹ khẩn cấp = 3 tháng chi thiết yếu). Mục tiêu người dùng tự đặt không cần mẫu.</summary>
public class GoalTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON object tham số mặc định của mẫu (vd số tháng chi thiết yếu). Không chứa số tiền cụ thể — số tiền do người dùng nhập.</summary>
    public string DefaultParams { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
