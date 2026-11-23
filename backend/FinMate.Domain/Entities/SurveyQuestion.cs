namespace FinMate.Domain.Entities;

/// <summary>
/// Một câu hỏi khảo sát đầu vào. Nằm trong DB để admin bỏ/sửa câu sau khi thử với người dùng thật
/// mà không cần deploy. <see cref="Code"/> bất biến: <c>user_survey_answers</c> lưu theo mã chữ.
/// </summary>
public class SurveyQuestion
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// JSON object điều kiện hiển thị theo câu trả lời trước (vd chỉ hỏi cách để dành khi mục tiêu là tiết kiệm). NULL = luôn hỏi.
    /// Định dạng: <c>{"all":[{"question":mã,"in":[mã đáp án]} | {"question":mã,"not_in":[mã đáp án]}]}</c> — mọi điều kiện phải đúng.
    /// Chỉ tham chiếu được câu hỏi đứng TRƯỚC (<see cref="SortOrder"/> nhỏ hơn).
    /// </summary>
    public string? ShowIf { get; set; }

    /// <summary><see cref="SurveyAnswerTypes"/>: chọn đáp án (mặc định) hoặc nhập số tiền.</summary>
    public string AnswerType { get; set; } = SurveyAnswerTypes.Choice;

    /// <summary>Danh mục hệ thống (slug) mà câu này nói về, dùng khi tính kế hoạch tháng. NULL = không gắn danh mục.</summary>
    public string? CategorySlug { get; set; }

    /// <summary>Vai trò trong kế hoạch tháng (<see cref="SurveyPlanRoles"/>). NULL = câu chỉ dùng để chọn phương pháp.</summary>
    public string? PlanRole { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
