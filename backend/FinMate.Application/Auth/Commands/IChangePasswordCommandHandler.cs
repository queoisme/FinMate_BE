namespace FinMate.Application.Auth.Commands;

public interface IChangePasswordCommandHandler
{
    Task HandleAsync(ChangePasswordCommand command, CancellationToken ct = default);
}
