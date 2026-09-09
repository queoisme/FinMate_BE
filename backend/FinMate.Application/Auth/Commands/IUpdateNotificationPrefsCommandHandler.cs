namespace FinMate.Application.Auth.Commands;

public interface IUpdateNotificationPrefsCommandHandler
{
    Task HandleAsync(UpdateNotificationPrefsCommand command, CancellationToken ct = default);
}
