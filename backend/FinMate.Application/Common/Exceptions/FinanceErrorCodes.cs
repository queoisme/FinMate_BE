namespace FinMate.Application.Common.Exceptions;

/// <summary>Mã lỗi của khảo sát, gợi ý và chọn cách quản lý tài chính.</summary>
public static class FinanceErrorCodes
{
    /// <summary>Xin gợi ý khi người dùng chưa trả lời câu nào của khảo sát (hoặc đã bỏ qua).</summary>
    public const string SurveyNotAnswered = "SURVEY_NOT_ANSWERED";

    /// <summary>Đáp án gửi lên không khớp câu hỏi/đáp án đang bật, trùng câu, hoặc danh sách rỗng.</summary>
    public const string SurveyAnswerInvalid = "SURVEY_ANSWER_INVALID";
    public const string FeasibilityInputInvalid = "FEASIBILITY_INPUT_INVALID";

    /// <summary>Bỏ qua khảo sát khi tài khoản đã làm xong hoặc đã bỏ qua trước đó.</summary>
    public const string OnboardingAlreadyDecided = "ONBOARDING_ALREADY_DECIDED";

    /// <summary>Không còn phương pháp chính nào đủ điều kiện sau khi lọc theo tín hiệu của người dùng.</summary>
    public const string NoMethodAvailable = "FINANCE_NO_METHOD_AVAILABLE";

    public const string MethodInactive = "FINANCE_METHOD_INACTIVE";

    /// <summary>Phương pháp không làm được vai trò đã chọn (vd chọn cách chỉ-bổ-trợ làm cách chính).</summary>
    public const string MethodRoleMismatch = "FINANCE_METHOD_ROLE_MISMATCH";

    /// <summary>Phương pháp đang được dùng ở vai trò còn lại; một phương pháp không giữ cả hai vai trò.</summary>
    public const string MethodAlreadyInUse = "FINANCE_METHOD_ALREADY_IN_USE";

    /// <summary>Tham số không tồn tại, không cho người dùng sửa, ngoài khoảng [min, max], hoặc làm T1/T2 sai thứ tự.</summary>
    public const string MethodParamInvalid = "FINANCE_METHOD_PARAM_INVALID";

    /// <summary>Ngưỡng cảnh báo cá nhân không thỏa 1 ≤ T1 &lt; T2 ≤ 99.</summary>
    public const string AlertThresholdInvalid = "ALERT_THRESHOLD_INVALID";

    public const string RecommendationAlreadyDecided = "FINANCE_RECOMMENDATION_ALREADY_DECIDED";
    public const string GoalTemplateInactive = "FINANCE_GOAL_TEMPLATE_INACTIVE";
}
