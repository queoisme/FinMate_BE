namespace FinMate.Application.Auth.Commands;

public interface IDeleteAccountCommandHandler
{
    Task HandleAsync(DeleteAccountCommand command, CancellationToken ct = default);
}
