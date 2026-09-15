namespace FinMate.Application.Auth.Commands;

public record VerifyEmailCommand(string Email, string Code);

public interface IVerifyEmailCommandHandler
{
    Task HandleAsync(VerifyEmailCommand command, CancellationToken ct = default);
}
