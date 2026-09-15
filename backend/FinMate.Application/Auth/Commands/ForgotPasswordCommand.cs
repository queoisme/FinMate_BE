namespace FinMate.Application.Auth.Commands;

public record ForgotPasswordCommand(string Email);

public interface IForgotPasswordCommandHandler
{
    Task HandleAsync(ForgotPasswordCommand command, CancellationToken ct = default);
}
