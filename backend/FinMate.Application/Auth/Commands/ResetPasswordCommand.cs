namespace FinMate.Application.Auth.Commands;

public record ResetPasswordCommand(string Email, string Code, string NewPassword);

public interface IResetPasswordCommandHandler
{
    Task HandleAsync(ResetPasswordCommand command, CancellationToken ct = default);
}
