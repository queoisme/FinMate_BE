using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Commands;

/// <param name="AdminId">Lấy từ JWT claims, không bao giờ từ body — CONVENTIONS.md §6.2.</param>
/// <param name="Role">Vai trò MONG MUỐN, không phải lệnh thăng/hạ — gọi lại cho cùng kết quả.</param>
public record SetUserRoleCommand(
    Guid AdminId,
    Guid TargetUserId,
    UserRole Role,
    string? Reason,
    string? IpAddress);
