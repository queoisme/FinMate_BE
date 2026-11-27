namespace FinMate.Application.Common;

/// <summary>
/// Kênh gọi một handler dùng chung giữa <c>/admin/*</c> và <c>/ops/*</c> (Phase 95, D13): chọn tên sự kiện audit. <c>/ops/audit-logs</c> chỉ hiện sự kiện <c>Ops.*</c>,
/// nên thao tác của Staff phải ghi <c>Ops.*</c> để Manager thấy; đường Admin giữ tên <c>Admin.*</c> như cũ.
/// </summary>
public enum AuditChannel
{
    Admin,
    Ops,
}
