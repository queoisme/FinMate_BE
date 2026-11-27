using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public interface IOpsNotificationRepository
{
    /// <summary>Tài khoản đang hoạt động (không khóa, không chờ xóa) có một trong các vai trò này.</summary>
    Task<IReadOnlyList<Guid>> ListActiveUserIdsByRolesAsync(IReadOnlyCollection<UserRole> roles, CancellationToken ct = default);

    /// <summary>Trong số <paramref name="userIds"/>, ai đang có thông báo CHƯA ĐỌC cùng loại cùng mục liên quan. Dùng để không dồn thông báo trùng.</summary>
    Task<IReadOnlySet<Guid>> ListUsersWithUnreadAsync(OpsNotificationType type, Guid? relatedEntityId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    Task AddRangeAsync(IReadOnlyList<OpsNotification> notifications, CancellationToken ct = default);

    Task<(IReadOnlyList<OpsNotification> Items, int Total)> ListAsync(Guid userId, bool unreadOnly, string? search, int skip, int take, CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Đánh dấu đã đọc thông báo của chính <paramref name="userId"/>. False nếu không có thông báo đó của người này.</summary>
    Task<bool> MarkReadAsync(Guid userId, Guid id, DateTimeOffset at, CancellationToken ct = default);

    Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
