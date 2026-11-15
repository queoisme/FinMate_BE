using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Legal;

namespace FinMate.Application.Auth.Queries;

public class GetUserProfileQueryHandler : IGetUserProfileQueryHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;
    private readonly ILegalVersionProvider _legal;

    public GetUserProfileQueryHandler(IUserRepository userRepository, ICacheService cacheService, ILegalVersionProvider legal)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
        _legal = legal;
    }

    public async Task<UserProfileDto> HandleAsync(GetUserProfileQuery query, CancellationToken ct = default)
    {
        var currentTerms = await _legal.CurrentTermsVersionAsync(ct);
        var cacheKey = CacheKeys.UserProfile(query.UserId);
        var cached = await _cacheService.GetAsync<UserProfileDto>(cacheKey, ct);

        // Profile nằm trong cache 15 phút, nhưng Admin có thể xuất bản Điều khoản mới ngay lúc đó: so lại với phiên bản hiện hành thay vì
        // tin cờ đã cache. Bản cache cũ (trước khi có TermsVersion) mà đã ghi "đã đồng ý" thì không so được, coi như không có trong cache.
        if (cached is not null && !(cached.TermsAccepted && cached.TermsVersion is null))
        {
            return cached with { TermsAccepted = cached.TermsVersion == currentTerms };
        }

        var user = await _userRepository.GetByIdAsync(query.UserId, ct)
            ?? throw new NotFoundException("User", query.UserId);

        var profile = UserProfileDto.From(user, currentTerms);

        await _cacheService.SetAsync(cacheKey, profile, CacheKeys.UserProfileTtl, ct);

        return profile;
    }
}
