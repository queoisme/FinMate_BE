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
}
