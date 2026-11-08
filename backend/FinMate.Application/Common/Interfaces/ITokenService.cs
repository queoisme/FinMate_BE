using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public record GeneratedRefreshToken(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    string GenerateAccessToken(User user);

    /// <summary><paramref name="opsVerified"/>: tài khoản nội bộ đã qua bước OTP.</summary>
    string GenerateAccessToken(User user, bool opsVerified);
    GeneratedRefreshToken GenerateRefreshToken();
    string HashToken(string rawToken);
}
