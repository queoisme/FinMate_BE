using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Queries;

public class GetUserProfileQueryHandler : IGetUserProfileQueryHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;

    public GetUserProfileQueryHandler(IUserRepository userRepository, ICacheService cacheService)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    public async Task<UserProfileDto> HandleAsync(GetUserProfileQuery query, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.UserProfile(query.UserId);
        var cached = await _cacheService.GetAsync<UserProfileDto>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var user = await _userRepository.GetByIdAsync(query.UserId, ct)
            ?? throw new NotFoundException("User", query.UserId);

        var profile = new UserProfileDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            user.MonthlyIncomeCents,
            user.NotificationPrefs.PushEnabled,
            user.NotificationPrefs.BudgetAlertsEnabled,
            user.NotificationPrefs.MissionRemindersEnabled);

        await _cacheService.SetAsync(cacheKey, profile, CacheKeys.UserProfileTtl, ct);

        return profile;
    }
}
