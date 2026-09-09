using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public record GeneratedRefreshToken(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    string GenerateAccessToken(User user);
    GeneratedRefreshToken GenerateRefreshToken();
    string HashToken(string rawToken);
}
