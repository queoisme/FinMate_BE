using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class UpdateNotificationPrefsCommandHandler : IUpdateNotificationPrefsCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;

    public UpdateNotificationPrefsCommandHandler(IUserRepository userRepository, ICacheService cacheService)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    public async Task HandleAsync(UpdateNotificationPrefsCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        user.NotificationPrefs.PushEnabled = command.PushEnabled;
        user.NotificationPrefs.BudgetAlertsEnabled = command.BudgetAlertsEnabled;
        user.NotificationPrefs.MissionRemindersEnabled = command.MissionRemindersEnabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);
    }
}
