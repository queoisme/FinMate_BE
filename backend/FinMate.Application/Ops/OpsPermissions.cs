using FinMate.Domain.Enums;

namespace FinMate.Application.Ops;

/// <summary>Việc một tài khoản nội bộ được làm trên web vận hành. Ma trận đầy đủ nằm ở <c>docs/ops-api.md</c>.</summary>
[Flags]
public enum OpsCapability
{
    None = 0,
    HandleTickets = 1 << 0,

    /// <summary>Xin và xem dữ liệu của khách. Có quyền này vẫn phải được chính khách đồng ý theo từng ca, kể cả Admin.</summary>
    AccessCustomerData = 1 << 1,
    CreateDraftTransactions = 1 << 2,
    EmergencyLock = 1 << 3,
    ProposeApprovals = 1 << 4,
    ManageTickets = 1 << 5,
    DecideApprovals = 1 << 6,
    ViewDashboard = 1 << 7,
    ManageCannedReplies = 1 << 8,
    ViewOpsAudit = 1 << 9,
    ViewAiStats = 1 << 10,
    ManageSystem = 1 << 11,
    ViewMonitoring = 1 << 12,
    ManageIncidents = 1 << 13,

    /// <summary>Duyệt ảnh, xử lý báo cáo, ẩn nội dung cộng đồng. Không kèm quyền thấy tác giả.</summary>
    ModerateCommunity = 1 << 14,
    MuteCommunityAuthors = 1 << 15,
}

public static class OpsPermissions
{
    private const OpsCapability StaffCapabilities =
        OpsCapability.HandleTickets
        | OpsCapability.AccessCustomerData
        | OpsCapability.CreateDraftTransactions
        | OpsCapability.EmergencyLock
        | OpsCapability.ProposeApprovals
        | OpsCapability.ModerateCommunity;

    private const OpsCapability ManagerCapabilities =
        StaffCapabilities
        | OpsCapability.ManageTickets
        | OpsCapability.DecideApprovals
        | OpsCapability.ViewDashboard
        | OpsCapability.ManageCannedReplies
        | OpsCapability.ViewOpsAudit
        | OpsCapability.ViewAiStats
        | OpsCapability.ViewMonitoring
        | OpsCapability.ManageIncidents
        | OpsCapability.MuteCommunityAuthors;

    public static OpsCapability For(UserRole role) => role switch
    {
        UserRole.Admin => ManagerCapabilities | OpsCapability.ManageSystem,
        UserRole.OpsManager => ManagerCapabilities,
        UserRole.Staff => StaffCapabilities,
        _ => OpsCapability.None,
    };

    /// <summary>Tên từng quyền cho web dựng menu.</summary>
    public static IReadOnlyList<string> Names(OpsCapability capabilities)
        => Enum.GetValues<OpsCapability>()
            .Where(c => c != OpsCapability.None && capabilities.HasFlag(c))
            .Select(c => c.ToString())
            .ToList();
}
