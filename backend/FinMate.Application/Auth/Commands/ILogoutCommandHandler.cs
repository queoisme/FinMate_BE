namespace FinMate.Application.Auth.Commands;

public interface ILogoutCommandHandler
{
    Task HandleAsync(LogoutCommand command, CancellationToken ct = default);
}
