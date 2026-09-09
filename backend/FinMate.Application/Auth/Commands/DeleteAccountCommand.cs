namespace FinMate.Application.Auth.Commands;

public record DeleteAccountCommand(Guid UserId, string Password);
