using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <param name="IncludeDeleted">
/// Bao gồm cả tài khoản đã soft delete. Mặc định false vì <c>User</c> có global query filter
/// <c>deleted_at IS NULL</c>; admin cần bật để đối chiếu yêu cầu xoá dữ liệu đang chờ 30 ngày.
/// </param>
public record UserListFilter(
    string? Search,
    UserRole? Role,
    bool? IsLocked,
    bool IncludeDeleted,
    string? Cursor,
    int Limit);

public record UserListResult(IReadOnlyList<User> Items, string? NextCursor);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);

    /// <summary>Danh sách cho màn hình quản trị — KHÔNG kèm dữ liệu tài chính (ARCHITECTURE.md §7.3).</summary>
    Task<UserListResult> GetPageAsync(UserListFilter filter, CancellationToken ct = default);
}
