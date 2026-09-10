namespace FinMate.Application.Notifications.Commands;

public record AnalyzeNotificationCommand(
    Guid UserId,
    string PackageName,
    string? NotificationTitle,
    string NotificationBody,
    DateTimeOffset ReceivedAt);
