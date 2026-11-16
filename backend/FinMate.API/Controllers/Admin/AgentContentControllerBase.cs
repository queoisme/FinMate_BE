using FinMate.API.Controllers.Ops;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Lớp cha của các controller nội dung của agent (giả định kinh tế, kho tri thức). Dùng hệ vai trò vận hành của Phase 61–65: Staff trở lên được soạn bản nháp và xem (policy StaffOrAbove nằm ở <see cref="OpsControllerBase"/>);
/// các hành động DUYỆT / GỠ gắn thêm <c>[Authorize(Policy = ManagerOrAbove)]</c> (OpsManager, Admin) và có test 403 riêng, vì thiếu attribute đó không gây lỗi nào.
/// </summary>
public abstract class AgentContentControllerBase : OpsControllerBase
{
    protected Guid CurrentAdminId => CurrentActorId;
}
