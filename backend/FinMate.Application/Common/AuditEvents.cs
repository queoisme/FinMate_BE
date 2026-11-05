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

    // Luật phân loại của AI (thương hiệu, từ khóa, bộ nhớ tên món, chỉ dẫn Gemini). Chỉ thay đổi THÀNH CÔNG mới ghi; nội dung trước/sau nằm ở nhật ký của AI Service.
    public const string AdminAiRuleChanged = "Admin.AiRule.Changed";
    /// <summary>Tài liệu cấu hình của AI/OCR (từ vựng chat, từ điển cửa hàng, ngưỡng...) được lưu bản nháp hoặc kích hoạt. Nội dung từng phiên bản do AI Service giữ (config_documents).</summary>
    public const string AdminAiConfigChanged = "Admin.AiConfig.Changed";
    /// <summary>Admin yêu cầu kích hoạt một phiên bản model Rasa (kể cả quay về bản cũ, kể cả ép qua cổng đo). AI Service nhận (202) rồi nạp ở nền; kết quả cuối và số đo nằm ở sổ <c>nlu_model_versions</c> / <c>activation</c> của <c>GET /admin/rasa/models</c>.</summary>
    public const string AdminRasaModelActivationRequested = "Admin.RasaModel.ActivationRequested";
    /// <summary>Admin thêm một phiên bản chỉ dẫn AI cho gợi ý phương thức. Nội dung chỉ dẫn nằm ở <c>kb_method_prompts</c> của AI Service, không ghi vào nhật ký chung.</summary>
    public const string AdminMethodPromptCreated = "Admin.MethodPrompt.Created";
    /// <summary>Admin chuyển bản chỉ dẫn AI đang dùng (kể cả quay về bản cũ).</summary>
    public const string AdminMethodPromptActivated = "Admin.MethodPrompt.Activated";
    /// <summary>Định dạng thông báo của một ngân hàng/ví (tạo, nhân bản, sửa pattern/trường/mẫu, kích hoạt, lưu trữ). Nội dung nằm ở nhật ký của AI Service; ở đây không ghi thân yêu cầu vì mẫu có thể chứa chữ thông báo.</summary>
    public const string AdminProviderFormatChanged = "Admin.ProviderFormat.Changed";
    /// <summary>Cấu hình hệ thống (bảo trì, phiên bản app, tính năng, giao diện) được lưu hoặc hoàn tác. Metadata chỉ có tên các phần đã đổi; nội dung nằm ở chính phiên bản trong <c>system_config_versions</c>.</summary>
    public const string AdminSystemConfigChanged = "Admin.SystemConfig.Changed";
    /// <summary>Key bên thứ ba được đặt hoặc xóa. Metadata chỉ có tên key, hành động và lý do: KHÔNG BAO GIỜ có giá trị key (AI Service cũng chỉ giữ 4 ký tự cuối).</summary>
    public const string AdminSecretChanged = "Admin.Secret.Changed";

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

    // Nhóm — metadata chỉ có id và vai trò, không có số tiền, email hay mã mời.
    public const string GroupCreated = "Group.Created";
    public const string GroupUpdated = "Group.Updated";
    public const string GroupDeleted = "Group.Deleted";
    public const string GroupMemberJoined = "Group.Member.Joined";
    public const string GroupMemberLeft = "Group.Member.Left";
    public const string GroupMemberRemoved = "Group.Member.Removed";
    public const string GroupMemberUpdated = "Group.Member.Updated";
    public const string GroupOwnershipTransferred = "Group.Ownership.Transferred";
    public const string GroupInvitationCreated = "Group.Invitation.Created";
    public const string GroupInvitationRevoked = "Group.Invitation.Revoked";

    // Vận hành. Chỉ ghi id và trạng thái, không ghi nội dung ticket.
    public const string OpsTicketClaimed = "Ops.Ticket.Claimed";
    public const string OpsTicketAssigned = "Ops.Ticket.Assigned";
    public const string OpsTicketStatusChanged = "Ops.Ticket.StatusChanged";
    public const string OpsTicketPriorityChanged = "Ops.Ticket.PriorityChanged";
    public const string OpsTicketEscalated = "Ops.Ticket.Escalated";
    public const string OpsTicketAttachmentViewed = "Ops.Ticket.AttachmentViewed";
    public const string OpsCustomerDataRequested = "Ops.CustomerData.Requested";
    public const string OpsCustomerDataViewed = "Ops.CustomerData.Viewed";
    public const string OpsDraftTransactionCreated = "Ops.CustomerData.DraftCreated";
    public const string OpsTicketsLinked = "Ops.Ticket.Linked";
    public const string OpsTicketsBroadcast = "Ops.Ticket.Broadcast";
    public const string OpsApprovalProposed = "Ops.Approval.Proposed";
    public const string OpsApprovalApproved = "Ops.Approval.Approved";
    public const string OpsApprovalRejected = "Ops.Approval.Rejected";
    public const string OpsApprovalFailed = "Ops.Approval.Failed";
    public const string OpsUserEmergencyLocked = "Ops.User.EmergencyLocked";
    public const string OpsGroupViewed = "Ops.Group.Viewed";
    public const string OpsGroupOwnerTransferred = "Ops.Group.OwnerTransferred";
    public const string OpsCannedReplyChanged = "Ops.CannedReply.Changed";
    public const string OpsIncidentAcknowledged = "Ops.Incident.Acknowledged";
    public const string OpsIncidentResolved = "Ops.Incident.Resolved";
    public const string OpsTwoFactorVerified = "Ops.TwoFactor.Verified";
    public const string OpsTwoFactorFailed = "Ops.TwoFactor.Failed";
}
