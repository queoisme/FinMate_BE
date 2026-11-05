using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace FinMate.Infrastructure.ExternalServices;

public class TokenService : ITokenService
{
    private readonly string _jwtSecret;
    private readonly int _accessTtlMinutes;
    private readonly int _refreshTtlDays;

    public TokenService(IConfiguration configuration)
    {
        _jwtSecret = configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        _accessTtlMinutes = int.Parse(configuration["JWT_ACCESS_TTL_MINUTES"] ?? "15");
        _refreshTtlDays = int.Parse(configuration["JWT_REFRESH_TTL_DAYS"] ?? "30");
    }

    public string GenerateAccessToken(User user) => GenerateAccessToken(user, opsVerified: false);

    public string GenerateAccessToken(User user, bool opsVerified)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        if (opsVerified)
        {
            claims.Add(new Claim(OpsClaims.Verified, OpsClaims.VerifiedValue));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTtlMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public GeneratedRefreshToken GenerateRefreshToken()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_refreshTtlDays);
        return new GeneratedRefreshToken(rawToken, HashToken(rawToken), expiresAt);
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
