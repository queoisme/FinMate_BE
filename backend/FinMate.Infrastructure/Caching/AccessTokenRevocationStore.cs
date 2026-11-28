using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.Caching;

public class AccessTokenRevocationStore : IAccessTokenRevocationStore
{
    private readonly ICacheService _cache;
    private readonly TimeSpan _ttl;
    private readonly ILogger<AccessTokenRevocationStore> _logger;
    private readonly TimeProvider _clock;

    public AccessTokenRevocationStore(
        ICacheService cache,
        IConfiguration configuration,
        ILogger<AccessTokenRevocationStore> logger,
        TimeProvider? clock = null)
    {
        _cache = cache;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        // Dư một phút để token cấp sát giây cuối vẫn nằm trong thời hạn lưu mốc.
        _ttl = TimeSpan.FromMinutes(int.Parse(configuration["JWT_ACCESS_TTL_MINUTES"] ?? "15") + 1);
    }

    private static string Key(Guid userId) => $"auth:revoked-before:{userId}";

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        // Mốc là giây NGUYÊN kế tiếp: iat chỉ có độ phân giải giây, nên mọi token cấp ở giây hiện
        // tại (trước hay sau lệnh này) bị thu hồi, còn token cấp từ giây kế tiếp thì hợp lệ.
        var mark = _clock.GetUtcNow().ToUnixTimeSeconds() + 1;
        try
        {
            await _cache.SetAsync(Key(userId), mark, _ttl, ct);
        }
        catch (Exception ex)
        {
            // Không để lỗi Redis làm hỏng lệnh khóa/hạ quyền đã ghi DB; chỉ ghi loại lỗi, không ghi gì khác.
            _logger.LogWarning("Could not record access token revocation ({ErrorType}).", ex.GetType().Name);
        }
    }

    public async Task<bool> IsRevokedAsync(Guid userId, long? issuedAtUnixSeconds, CancellationToken ct = default)
    {
        try
        {
            var mark = await _cache.GetAsync<long?>(Key(userId), ct);
            if (mark is null)
            {
                return false;
            }

            // Token không có iat (cấp trước khi có claim này) không so được: thu hồi, tối đa mất một TTL.
            return issuedAtUnixSeconds is null || issuedAtUnixSeconds.Value < mark.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not check access token revocation ({ErrorType}); allowing.", ex.GetType().Name);
            return false;
        }
    }
}
