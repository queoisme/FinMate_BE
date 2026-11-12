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
}

/// <summary>
/// Thẻ đề xuất hành động cho người dùng. Do CODE của công cụ sinh ra từ dữ liệu có cấu trúc, KHÔNG do LLM viết: <c>Title</c> và số liệu đều lấy từ bằng chứng của chính lượt này
/// (<c>EvidenceIds</c> chỉ tới chúng), nên thẻ không thể mang con số không có nguồn. Thẻ chỉ là đề xuất: app hiện nút, người dùng bấm xác nhận thì app gọi API sẵn có
/// (ví dụ tạo hạn mức ở <c>/budgets</c>); agent không tự thay đổi dữ liệu nào.
/// </summary>
/// <param name="CategorySlug">Slug nhóm chi hệ thống (app tự đổi sang mã nhóm của người dùng); chỉ có ở <c>set_budget_limit</c>.</param>
public sealed record SuggestedActionDto(string Kind, string Title, long? AmountVnd, string? Date, string? CategorySlug, string? CategoryName, IReadOnlyList<string> EvidenceIds);
