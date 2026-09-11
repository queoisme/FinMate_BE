using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities.Gamification;

namespace FinMate.Application.Gamification.Queries;

public class GetGamificationProfileQueryHandler : IGetGamificationProfileQueryHandler
{
    private readonly IGamificationRepository _gamificationRepository;
    private readonly ICacheService _cache;

    public GetGamificationProfileQueryHandler(IGamificationRepository gamificationRepository, ICacheService cache)
    {
        _gamificationRepository = gamificationRepository;
        _cache = cache;
    }

    public async Task<GamificationProfileDto> HandleAsync(
        GetGamificationProfileQuery query,
        CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.Gamification(query.UserId);
        var cached = await _cache.GetAsync<GamificationProfileDto>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        // Chỉ đọc: user chưa hoạt động lần nào thì trả hồ sơ rỗng chứ không ghi dòng mới —
        // dòng user_gamification được tạo lazily bởi GamificationService khi có hoạt động thật.
        var profile = await _gamificationRepository.GetProfileAsync(query.UserId, ct)
            ?? new UserGamification { UserId = query.UserId };

        var dto = GamificationMapper.ToDto(profile);
        await _cache.SetAsync(cacheKey, dto, CacheKeys.GamificationTtl, ct);

        return dto;
    }
}
