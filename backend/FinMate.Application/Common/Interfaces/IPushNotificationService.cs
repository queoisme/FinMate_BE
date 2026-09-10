namespace FinMate.Application.Common.Interfaces;

public interface IPushNotificationService
{
    Task NotifyAsync(Guid userId, string title, string body, CancellationToken ct = default);
}
