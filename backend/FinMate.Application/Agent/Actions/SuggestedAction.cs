namespace FinMate.Application.Agent;

/// <summary>Các loại thẻ đề xuất hành động (tên trên dây, cùng bộ giá trị app dùng để chọn nút).</summary>
public static class SuggestedActionKinds
{
    /// <summary>Hoãn khoản mua tới ngày sớm nhất vẫn an toàn (<c>Date</c>, định dạng yyyy-MM-dd).</summary>
    public const string WaitUntil = "wait_until";

    /// <summary>Giảm số tiền mua xuống mức tối đa còn giữ được vùng đệm (<c>AmountVnd</c>).</summary>
    public const string LowerAmountTo = "lower_amount_to";

    /// <summary>Đặt hạn mức chi mỗi tháng cho một nhóm (<c>CategorySlug</c>, <c>AmountVnd</c>), lấy từ kết quả chia ngân sách.</summary>
    public const string SetBudgetLimit = "set_budget_limit";

    /// <summary>Tạo mục tiêu tiết kiệm cho số tiền <c>AmountVnd</c> với hạn <c>Date</c> (yyyy-MM-dd), lấy từ câu hỏi "mua X trong N tháng được không". App mở thẻ mục tiêu cho người dùng sửa rồi xác nhận.</summary>
    public const string CreateGoal = "create_goal";

    /// <summary>Đổi hạn của mục tiêu đã có (<c>TargetId</c> = id mục tiêu) sang <c>Date</c>; <c>AmountVnd</c> là số cần góp mỗi tháng theo hạn mới. Lấy từ công cụ <c>goal_plan</c> khi mục tiêu chưa khả thi với hạn cũ.</summary>
    public const string ChangeGoalDeadline = "change_goal_deadline";
}

/// <summary>
/// Thẻ đề xuất hành động cho người dùng. Do CODE của công cụ sinh ra từ dữ liệu có cấu trúc, KHÔNG do LLM viết: <c>Title</c> và số liệu đều lấy từ bằng chứng của chính lượt này
/// (<c>EvidenceIds</c> chỉ tới chúng), nên thẻ không thể mang con số không có nguồn. Thẻ chỉ là đề xuất: app hiện nút, người dùng bấm xác nhận thì app gọi API sẵn có
/// (ví dụ tạo hạn mức ở <c>/budgets</c>); agent không tự thay đổi dữ liệu nào.
/// </summary>
/// <param name="CategorySlug">Slug nhóm chi hệ thống (app tự đổi sang mã nhóm của người dùng); chỉ có ở <c>set_budget_limit</c>.</param>
/// <param name="TargetId">Id đối tượng bị tác động (vd mục tiêu của <c>change_goal_deadline</c>); app chỉ dùng id này với API của chính người dùng.</param>
public sealed record SuggestedActionDto(string Kind, string Title, long? AmountVnd, string? Date, string? CategorySlug, string? CategoryName, IReadOnlyList<string> EvidenceIds, string? TargetId = null);
