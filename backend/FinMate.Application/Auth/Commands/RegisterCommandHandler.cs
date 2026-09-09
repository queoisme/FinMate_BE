using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Auth.Commands;

public class RegisterCommandHandler : IRegisterCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogService _auditLogService;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditLogService auditLogService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _auditLogService = auditLogService;
    }

    public async Task<UserProfileDto> HandleAsync(RegisterCommand command, CancellationToken ct = default)
    {
        if (await _userRepository.ExistsByEmailAsync(command.Email, ct))
        {
            throw new ConflictException(AuthErrorCodes.EmailAlreadyExists, "Email đã được sử dụng.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Email = command.Email,
            PasswordHash = _passwordHasher.Hash(command.Password),
            DisplayName = command.DisplayName,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _userRepository.AddAsync(user, ct);
        await _auditLogService.LogAsync("Auth.User.Registered", user.Id, ct: ct);

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            user.NotificationPrefs.PushEnabled,
            user.NotificationPrefs.BudgetAlertsEnabled,
            user.NotificationPrefs.MissionRemindersEnabled);
    }
}
