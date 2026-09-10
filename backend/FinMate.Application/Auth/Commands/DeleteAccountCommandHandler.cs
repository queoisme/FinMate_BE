using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class DeleteAccountCommandHandler : IDeleteAccountCommandHandler
{
    private const int RetentionDays = 30;

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IDataDeletionRequestRepository _dataDeletionRequestRepository;
    private readonly IAuditLogService _auditLogService;

    public DeleteAccountCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IRefreshTokenRepository refreshTokenRepository,
        IDataDeletionRequestRepository dataDeletionRequestRepository,
        IAuditLogService auditLogService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _refreshTokenRepository = refreshTokenRepository;
        _dataDeletionRequestRepository = dataDeletionRequestRepository;
        _auditLogService = auditLogService;
    }

    public async Task HandleAsync(DeleteAccountCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        if (user.PasswordHash is null)
        {
            throw new AuthenticationException(
                AuthErrorCodes.InvalidCredentials, "Tài khoản này chưa có mật khẩu (đăng nhập qua Google).");
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            throw new AuthenticationException(AuthErrorCodes.InvalidCredentials, "Mật khẩu không đúng.");
        }

        var now = DateTimeOffset.UtcNow;

        await _dataDeletionRequestRepository.AddAsync(new DataDeletionRequest
        {
            UserId = user.Id,
            RequestedAt = now,
            ScheduledHardDeleteAt = now.AddDays(RetentionDays),
        }, ct);

        user.DeletedAt = now;
        user.UpdatedAt = now;
        await _userRepository.UpdateAsync(user, ct);

        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);

        await _auditLogService.LogAsync("Auth.AccountDeletionRequested", user.Id, ct: ct);
    }
}
