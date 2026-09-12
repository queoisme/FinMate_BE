using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class ChangePasswordCommandHandler : IChangePasswordCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<ChangePasswordCommand> _validator;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogService auditLogService,
        IValidator<ChangePasswordCommand> validator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _refreshTokenRepository = refreshTokenRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        if (user.PasswordHash is null)
        {
            throw new AuthenticationException(
                AuthErrorCodes.InvalidCredentials, "Tài khoản này chưa có mật khẩu (đăng nhập qua Google).");
        }

        if (!_passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            throw new AuthenticationException(AuthErrorCodes.InvalidCredentials, "Mật khẩu hiện tại không đúng.");
        }

        user.PasswordHash = _passwordHasher.Hash(command.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        // Revoke every existing session — a password change must not leave old sessions valid.
        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);

        await _auditLogService.LogAsync(AuditEvents.PasswordChanged, user.Id, ct: ct);
    }
}
