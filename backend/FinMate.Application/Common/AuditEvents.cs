namespace FinMate.Application.Common;

/// <summary>
/// Tên sự kiện ghi vào <c>audit_logs.event_type</c>.
///
/// Gom về một chỗ vì <c>GET /api/v1/admin/audit-logs</c> cho lọc theo chính giá trị này:
/// chuỗi rải rác trong từng handler thì không ai biết bộ giá trị hợp lệ gồm những gì, và một
/// lỗi chính tả sẽ tạo ra một loại sự kiện mới mà không ai nhận ra (AGENTS.md §3.4).
///
/// Quy ước: <c>{Domain}.{Đối tượng}.{Hành động}</c>, PascalCase.
/// </summary>
public static class AuditEvents
{
    // Auth — đã tồn tại từ Phase 1, chuyển từ chuỗi rời sang hằng.
    public const string UserRegistered = "Auth.User.Registered";
    public const string LoginSuccess = "Auth.Login.Success";
    public const string LoginFailed = "Auth.Login.Failed";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string PasswordReset = "Auth.PasswordReset";
    public const string EmailVerified = "Auth.EmailVerified";
    public const string LogoutAllDevices = "Auth.LogoutAllDevices";
    public const string AccountDeletionRequested = "Auth.AccountDeletionRequested";
    public const string TokenReuseDetected = "Auth.TokenReuse.Detected";
    public const string GoogleLoginSuccess = "Auth.Google.Login.Success";
    public const string GoogleLinked = "Auth.Google.Linked";
    public const string GoogleRegistered = "Auth.Google.Registered";

    // Admin — Phase 8. Mọi thao tác GHI của admin đều để lại một dòng ở đây; đọc thì không,
    // nếu không log sẽ đầy những dòng "admin đã mở trang danh sách".
    public const string AdminUserLocked = "Admin.User.Locked";
    public const string AdminUserUnlocked = "Admin.User.Unlocked";
    public const string AdminUserRoleChanged = "Admin.User.RoleChanged";
    public const string AdminDataDeletionCancelled = "Admin.DataDeletionRequest.Cancelled";

    public const string AdminProviderConfigCreated = "Admin.ProviderConfig.Created";
    public const string AdminProviderConfigUpdated = "Admin.ProviderConfig.Updated";
    public const string AdminProviderConfigActivationChanged = "Admin.ProviderConfig.ActivationChanged";

    public const string AdminCategoryCreated = "Admin.Category.Created";
    public const string AdminCategoryUpdated = "Admin.Category.Updated";
    public const string AdminCategoryActivationChanged = "Admin.Category.ActivationChanged";

    public const string AdminMissionCreated = "Admin.Mission.Created";
    public const string AdminMissionUpdated = "Admin.Mission.Updated";
    public const string AdminMissionActivationChanged = "Admin.Mission.ActivationChanged";

    // Luật phân loại của AI (thương hiệu, từ khóa, bộ nhớ tên món, chỉ dẫn Gemini). Chỉ thay đổi THÀNH CÔNG mới ghi; nội dung trước/sau nằm ở nhật ký của AI Service.
    public const string AdminAiRuleChanged = "Admin.AiRule.Changed";
    /// <summary>Tài liệu cấu hình của AI/OCR (từ vựng chat, từ điển cửa hàng, ngưỡng...) được lưu bản nháp hoặc kích hoạt. Nội dung từng phiên bản do AI Service giữ (config_documents).</summary>
    public const string AdminAiConfigChanged = "Admin.AiConfig.Changed";

    // Danh mục cách quản lý tài chính, khảo sát đầu vào và mẫu mục tiêu. Chỉ ghi khi có thay đổi thật (gửi lại đúng nội dung cũ không để lại dòng nào).
    public const string AdminFinanceMethodCreated = "Admin.FinanceMethod.Created";
    public const string AdminFinanceMethodUpdated = "Admin.FinanceMethod.Updated";
    public const string AdminFinanceMethodActivationChanged = "Admin.FinanceMethod.ActivationChanged";
    public const string AdminSurveyQuestionCreated = "Admin.SurveyQuestion.Created";
    public const string AdminSurveyQuestionUpdated = "Admin.SurveyQuestion.Updated";
    public const string AdminSurveyQuestionActivationChanged = "Admin.SurveyQuestion.ActivationChanged";
    public const string AdminSurveyOptionCreated = "Admin.SurveyOption.Created";
    public const string AdminSurveyOptionUpdated = "Admin.SurveyOption.Updated";
    public const string AdminSurveyOptionActivationChanged = "Admin.SurveyOption.ActivationChanged";
    public const string AdminGoalTemplateCreated = "Admin.GoalTemplate.Created";
    public const string AdminGoalTemplateUpdated = "Admin.GoalTemplate.Updated";
    public const string AdminGoalTemplateActivationChanged = "Admin.GoalTemplate.ActivationChanged";
    public const string AdminMethodGoalTemplatesReplaced = "Admin.MethodGoalTemplates.Replaced";
}
