using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class LogoutCommandHandler : ILogoutCommandHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokenRepository, ITokenService tokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
    }

    public async Task HandleAsync(LogoutCommand command, CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashToken(command.RefreshToken);
        var existing = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, ct);

        if (existing is null || existing.RevokedAt is not null)
        {
            // Already logged out — idempotent, no error.
            return;
        }

        await _refreshTokenRepository.RevokeAsync(existing, ct: ct);
        await _refreshTokenRepository.SaveChangesAsync(ct);
    }
}
