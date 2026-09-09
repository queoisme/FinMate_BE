using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class LoginCommandHandler : ILoginCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IAuditLogService _auditLogService;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IAuditLogService auditLogService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _auditLogService = auditLogService;
    }

    public async Task<AuthResultDto> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(command.Email, ct);

        if (user is null || !_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            await _auditLogService.LogAsync(
                "Auth.Login.Failed", user?.Id, command.IpAddress, new { command.Email }, ct);
            throw new AuthenticationException(AuthErrorCodes.InvalidCredentials, "Email hoặc mật khẩu không đúng.");
        }

        if (user.IsLocked)
        {
            await _auditLogService.LogAsync("Auth.Login.Failed", user.Id, command.IpAddress, ct: ct);
            throw new AuthenticationException(AuthErrorCodes.AccountLocked, "Tài khoản đã bị khóa.");
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refresh = _tokenService.GenerateRefreshToken();

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        await _auditLogService.LogAsync("Auth.Login.Success", user.Id, command.IpAddress, ct: ct);

        return new AuthResultDto(accessToken, refresh.RawToken, refresh.ExpiresAt);
    }
}
