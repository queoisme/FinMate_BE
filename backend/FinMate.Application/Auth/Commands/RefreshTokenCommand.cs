namespace FinMate.Application.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken, string? IpAddress = null);
