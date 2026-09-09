using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public class UpdateUserProfileCommandHandler : IUpdateUserProfileCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;

    public UpdateUserProfileCommandHandler(IUserRepository userRepository, ICacheService cacheService)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    public async Task<UserProfileDto> HandleAsync(UpdateUserProfileCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        user.DisplayName = command.DisplayName;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);

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
