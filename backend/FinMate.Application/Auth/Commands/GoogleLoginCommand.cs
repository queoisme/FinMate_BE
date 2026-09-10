namespace FinMate.Application.Auth.Commands;

public record GoogleLoginCommand(string IdToken, string? IpAddress);
