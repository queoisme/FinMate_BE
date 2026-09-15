namespace FinMate.Application.Auth.Commands;

public record SendEmailVerificationCommand(string Email);

public interface ISendEmailVerificationCommandHandler
{
    Task HandleAsync(SendEmailVerificationCommand command, CancellationToken ct = default);
}
