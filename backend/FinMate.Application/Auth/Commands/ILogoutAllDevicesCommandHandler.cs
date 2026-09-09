namespace FinMate.Application.Auth.Commands;

public interface ILogoutAllDevicesCommandHandler
{
    Task HandleAsync(LogoutAllDevicesCommand command, CancellationToken ct = default);
}
