using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class ConsentCommandHandler : IConsentCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;

    public ConsentCommandHandler(IUserRepository userRepository, ICacheService cacheService)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
    }

    public async Task<UserProfileDto> AcceptAsync(AcceptConsentCommand command, CancellationToken ct = default)
    {
        var current = command.Kind == ConsentKind.Terms ? ConsentVersions.Terms : ConsentVersions.Ai;
        if (command.Version != current)
        {
            throw new ConflictException(AuthErrorCodes.ConsentVersionOutdated,
                "Nội dung chính sách đã thay đổi. Hãy cập nhật ứng dụng để xem và đồng ý bản mới.");
        }

        var user = await LoadAsync(command.UserId, ct);
        var now = DateTimeOffset.UtcNow;
        if (command.Kind == ConsentKind.Terms)
        {
            user.TermsVersion = current;
            user.TermsAcceptedAt = now;
        }
        else
        {
            user.AiConsentVersion = current;
            user.AiConsentAt = now;
        }

        return await SaveAsync(user, ct);
    }

    public async Task<UserProfileDto> WithdrawAiAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadAsync(userId, ct);
        user.AiConsentVersion = null;
        user.AiConsentAt = null;
        return await SaveAsync(user, ct);
    }

    private async Task<User> LoadAsync(Guid userId, CancellationToken ct) =>
        await _userRepository.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

    private async Task<UserProfileDto> SaveAsync(User user, CancellationToken ct)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);
        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);
        return UserProfileDto.From(user);
    }
}
