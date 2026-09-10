using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class GoogleLoginCommandHandler : IGoogleLoginCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly ITokenService _tokenService;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<GoogleLoginCommand> _validator;

    public GoogleLoginCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IGoogleTokenVerifier googleTokenVerifier,
        ITokenService tokenService,
        IAuditLogService auditLogService,
        IValidator<GoogleLoginCommand> validator)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _googleTokenVerifier = googleTokenVerifier;
        _tokenService = tokenService;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AuthResultDto> HandleAsync(GoogleLoginCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var googleUser = await _googleTokenVerifier.VerifyAsync(command.IdToken, ct);
        if (googleUser is null || !googleUser.EmailVerified)
        {
            throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Google ID token không hợp lệ.");
        }

        var user = await ResolveUserAsync(googleUser, ct);

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

        await _auditLogService.LogAsync("Auth.Google.Login.Success", user.Id, command.IpAddress, ct: ct);

        return new AuthResultDto(accessToken, refresh.RawToken, refresh.ExpiresAt);
    }

    private async Task<User> ResolveUserAsync(GoogleUserInfo googleUser, CancellationToken ct)
    {
        var byGoogleId = await _userRepository.GetByGoogleIdAsync(googleUser.Sub, ct);
        if (byGoogleId is not null)
        {
            return byGoogleId;
        }

        var byEmail = await _userRepository.GetByEmailAsync(googleUser.Email, ct);
        if (byEmail is not null)
        {
            // Account already exists via email/password — Google has verified ownership of this
            // email, so it's safe to auto-link rather than forcing a separate manual-link flow.
            byEmail.GoogleId = googleUser.Sub;
            byEmail.UpdatedAt = DateTimeOffset.UtcNow;
            await _userRepository.UpdateAsync(byEmail, ct);
            await _auditLogService.LogAsync("Auth.Google.Linked", byEmail.Id, ct: ct);
            return byEmail;
        }

        var now = DateTimeOffset.UtcNow;
        var newUser = new User
        {
            Email = googleUser.Email,
            PasswordHash = null,
            GoogleId = googleUser.Sub,
            DisplayName = string.IsNullOrWhiteSpace(googleUser.Name) ? googleUser.Email.Split('@')[0] : googleUser.Name,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _userRepository.AddAsync(newUser, ct);
        await _auditLogService.LogAsync("Auth.Google.Registered", newUser.Id, ct: ct);
        return newUser;
    }
}
