using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class UpdateUserProfileCommandHandler : IUpdateUserProfileCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;
    private readonly IValidator<UpdateUserProfileCommand> _validator;

    public UpdateUserProfileCommandHandler(
        IUserRepository userRepository,
        ICacheService cacheService,
        IValidator<UpdateUserProfileCommand> validator)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
        _validator = validator;
    }

    public async Task<UserProfileDto> HandleAsync(UpdateUserProfileCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        user.DisplayName = command.DisplayName;

        if (command.MonthlyIncomeCents is { } income)
        {
            // 0 nghĩa là "xoá khai báo", không phải "thu nhập bằng 0" — cả hai đều làm mọi
            // phép so sánh với thu nhập bị bỏ qua, nên gộp về NULL cho một trạng thái duy nhất.
            user.MonthlyIncomeCents = income > 0 ? income : null;
        }

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            user.MonthlyIncomeCents,
            user.NotificationPrefs.PushEnabled,
            user.NotificationPrefs.BudgetAlertsEnabled,
            user.NotificationPrefs.MissionRemindersEnabled);
    }
}
