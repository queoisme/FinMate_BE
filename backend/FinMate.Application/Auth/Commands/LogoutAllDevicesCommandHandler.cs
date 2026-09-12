using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class LogoutAllDevicesCommandHandler : ILogoutAllDevicesCommandHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAuditLogService _auditLogService;

    public LogoutAllDevicesCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogService auditLogService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _auditLogService = auditLogService;
    }

    public async Task HandleAsync(LogoutAllDevicesCommand command, CancellationToken ct = default)
    {
        await _refreshTokenRepository.RevokeAllForUserAsync(command.UserId, ct);
        await _auditLogService.LogAsync(AuditEvents.LogoutAllDevices, command.UserId, ct: ct);
    }
}
