using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Hộp thư thông báo trong app của một user (Phân hệ B, màn B2). Mọi hàm đọc/ghi đều lọc theo <c>userId</c>.</summary>
public interface IUserNotificationRepository
{
    Task AddAsync(UserNotification notification, CancellationToken ct = default);

    /// <summary>Toàn bộ thông báo của user, mới nhất trước — lọc theo tab và tính số đếm đều làm ở tầng service.</summary>
    Task<IReadOnlyList<UserNotification>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    Task<UserNotification?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task UpdateAsync(UserNotification notification, CancellationToken ct = default);

    Task DeleteAsync(UserNotification notification, CancellationToken ct = default);

    /// <summary>Đánh dấu mọi thông báo chưa đọc của user là đã đọc. Trả số bản ghi bị đổi.</summary>
    Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset readAt, CancellationToken ct = default);

    /// <summary>Xóa toàn bộ thông báo của user. Trả số bản ghi bị xóa.</summary>
    Task<int> DeleteAllAsync(Guid userId, CancellationToken ct = default);
}
