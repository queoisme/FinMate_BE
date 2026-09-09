namespace FinMate.Application.Auth.Commands;

public record LoginCommand(string Email, string Password, string? IpAddress = null);
