namespace FinMate.Application.Common.Models;

public record AuthResultDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
