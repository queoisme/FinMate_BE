using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Notifications;

public sealed record UserNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationTabCounts(int Total, int Unread);

/// <param name="All">Đếm trên TOÀN BỘ thông báo, không phụ thuộc tab đang xem — để FE vẽ số ở cả 3 tab cùng lúc.</param>
public sealed record UserNotificationListDto(
    IReadOnlyList<UserNotificationDto> Items,
    NotificationTabCounts All,
    NotificationTabCounts Movement,
    NotificationTabCounts AiAlert);

public interface IUserNotificationService
{
    Task CreateAsync(
        Guid userId,
        UserNotificationType type,
        string title,
        string body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct = default);

    /// <summary><paramref name="tab"/>: <c>all</c>, <c>movement</c> hoặc <c>ai_alert</c> (mặc định <c>all</c> nếu giá trị khác).</summary>
    Task<UserNotificationListDto> GetMineAsync(Guid userId, string tab, CancellationToken ct = default);

    Task<UserNotificationDto> MarkReadAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Trả số thông báo vừa được đánh dấu đã đọc.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Trả số thông báo vừa bị xóa.</summary>
    Task<int> DeleteAllAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Hộp thư thông báo trong app (Phân hệ B, màn B2): "Tất cả", "Biến động", "Cảnh báo AI". Tab cố định theo loại, không cấu hình được —
/// xem <see cref="MovementTypes"/>/<see cref="AiAlertTypes"/>.
/// </summary>
public class UserNotificationService : IUserNotificationService
{
    private static readonly HashSet<UserNotificationType> MovementTypes =
        [UserNotificationType.BalanceChange, UserNotificationType.GoalMilestone];

    private static readonly HashSet<UserNotificationType> AiAlertTypes =
        [UserNotificationType.BudgetThreshold, UserNotificationType.AnomalyDetected, UserNotificationType.RecurringBillDue, UserNotificationType.ForecastReady];

    private const string TabMovement = "movement";
    private const string TabAiAlert = "ai_alert";

    private readonly IUserNotificationRepository _repository;

    public UserNotificationService(IUserNotificationRepository repository)
    {
        _repository = repository;
    }

    public Task CreateAsync(
        Guid userId,
        UserNotificationType type,
        string title,
        string body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct = default)
        => _repository.AddAsync(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

    public async Task<UserNotificationListDto> GetMineAsync(Guid userId, string tab, CancellationToken ct = default)
    {
        var all = await _repository.ListForUserAsync(userId, ct);

        var items = tab switch
        {
            TabMovement => all.Where(n => MovementTypes.Contains(n.Type)),
            TabAiAlert => all.Where(n => AiAlertTypes.Contains(n.Type)),
            _ => all,
        };

        return new UserNotificationListDto(
            items.Select(ToDto).ToList(),
            CountOf(all),
            CountOf(all.Where(n => MovementTypes.Contains(n.Type))),
            CountOf(all.Where(n => AiAlertTypes.Contains(n.Type))));
    }

    public async Task<UserNotificationDto> MarkReadAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var notification = await _repository.GetAsync(id, userId, ct) ?? throw new NotFoundException("UserNotification", id);
        if (notification.ReadAt is null)
        {
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(notification, ct);
        }

        return ToDto(notification);
    }

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => _repository.MarkAllReadAsync(userId, DateTimeOffset.UtcNow, ct);

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var notification = await _repository.GetAsync(id, userId, ct) ?? throw new NotFoundException("UserNotification", id);
        await _repository.DeleteAsync(notification, ct);
    }

    public Task<int> DeleteAllAsync(Guid userId, CancellationToken ct = default)
        => _repository.DeleteAllAsync(userId, ct);

    private static NotificationTabCounts CountOf(IEnumerable<UserNotification> notifications)
    {
        var list = notifications as IReadOnlyCollection<UserNotification> ?? notifications.ToList();
        return new NotificationTabCounts(list.Count, list.Count(n => n.ReadAt is null));
    }

    private static UserNotificationDto ToDto(UserNotification n) => new(
        n.Id, n.Type.ToString(), n.Title, n.Body, n.RelatedEntityType, n.RelatedEntityId, n.CreatedAt, n.ReadAt);
}
