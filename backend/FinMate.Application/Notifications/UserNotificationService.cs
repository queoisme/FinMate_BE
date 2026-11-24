using System.Text.Json;
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
    DateTimeOffset? ReadAt,
    IReadOnlyDictionary<string, string>? Data = null);

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
    /// <summary>
    /// Lưu một push vừa gửi vào hộp thư, kèm đúng <paramref name="data"/> của push để bấm vào mở cùng màn. Loại và thực thể suy từ
    /// <c>data</c>; nếu nơi gửi đã tự ghi một mục cùng loại cùng thực thể (cảnh báo hóa đơn, chi tiêu bất thường) thì gắn data vào mục đó
    /// thay vì thêm mục thứ hai.
    /// </summary>
    Task RecordPushAsync(Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken ct = default);

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
        [UserNotificationType.BalanceChange, UserNotificationType.GoalMilestone, UserNotificationType.TransactionReview];

    private static readonly HashSet<UserNotificationType> AiAlertTypes =
        [UserNotificationType.BudgetThreshold, UserNotificationType.AnomalyDetected, UserNotificationType.RecurringBillDue, UserNotificationType.ForecastReady, UserNotificationType.ForecastAlert];

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

    // Cột title/body có giới hạn độ dài; push dài hơn thì cắt, không để cả lần ghi hỏng.
    private const int TitleMax = 120;
    private const int BodyMax = 500;

    // Mục do nơi gửi tự ghi trước push (cảnh báo dự báo đi qua outbox, có thể đẩy trễ tới 08:00) vẫn được ghép trong khoảng này.
    private static readonly TimeSpan MergeWindow = TimeSpan.FromDays(2);

    public async Task RecordPushAsync(
        Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        var (type, entityType, entityId) = Classify(data);
        var json = data is { Count: > 0 } ? JsonSerializer.Serialize(data) : null;

        if (entityId is { } id && json is not null
            && await _repository.FindWithoutPushAsync(userId, type, id, DateTimeOffset.UtcNow - MergeWindow, ct) is { } existing)
        {
            existing.Data = json;
            await _repository.UpdateAsync(existing, ct);
            return;
        }

        await _repository.AddAsync(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = Cut(title, TitleMax),
            Body = Cut(body, BodyMax),
            RelatedEntityType = entityType,
            RelatedEntityId = entityId,
            Data = json,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    /// <summary>
    /// Loại hộp thư + thực thể liên quan của một push, suy từ <c>type</c>/<c>action</c> mà nơi gửi đặt vào data (cùng khóa app dùng
    /// để mở màn khi bấm push, xem routeForPush ở FinMate_FE).
    /// </summary>
    internal static (UserNotificationType Type, string? EntityType, Guid? EntityId) Classify(IReadOnlyDictionary<string, string>? data)
    {
        string? Get(string key) => data is not null && data.TryGetValue(key, out var v) ? v : null;
        Guid? Id(string key) => Guid.TryParse(Get(key), out var g) ? g : null;

        return (Get("type") ?? Get("action")) switch
        {
            "transaction_review" => (UserNotificationType.TransactionReview, "Transaction", Id("transactionId")),
            "transaction_auto_committed" => (UserNotificationType.BalanceChange, "Transaction", Id("transactionId")),
            "budget_alert" => (UserNotificationType.BudgetThreshold, null, null),
            "goal_completed" or "goal_deadline" => (UserNotificationType.GoalMilestone, "SavingGoal", Id("goalId")),
            "loan_due" => (UserNotificationType.General, "Loan", Id("loanId")),
            "forecast_alert" => Get("kind") switch
            {
                "bill_due" or "bill_status_check" => (UserNotificationType.RecurringBillDue, "RecurringItem", Id("recurringItemId")),
                "anomaly" => (UserNotificationType.AnomalyDetected, "Transaction", Id("transactionId")),
                "budget_risk" => (UserNotificationType.BudgetThreshold, null, null),
                "weekly_digest" => (UserNotificationType.ForecastReady, null, null),
                _ => (UserNotificationType.ForecastAlert, null, null),
            },
            _ => (UserNotificationType.General, null, null),
        };
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

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
        n.Id, n.Type.ToString(), n.Title, n.Body, n.RelatedEntityType, n.RelatedEntityId, n.CreatedAt, n.ReadAt,
        n.Data is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(n.Data));
}
