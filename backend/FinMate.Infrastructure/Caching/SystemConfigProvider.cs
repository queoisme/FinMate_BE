using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Infrastructure.Caching;

/// <summary>
/// Cấu hình hệ thống đang chạy trong bộ nhớ đệm của TIẾN TRÌNH, hết hạn sau vài giây. Cố ý không dùng Redis: middleware bảo trì hỏi mỗi request và
/// một lần Redis chậm không được làm chậm mọi API. Có nhiều máy chủ thì máy khác thấy thay đổi chậm nhất bằng thời gian hết hạn.
/// </summary>
public class SystemConfigProvider : ISystemConfigProvider
{
    private const string CacheKey = "system-config:app";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;

    public SystemConfigProvider(IMemoryCache cache, IServiceScopeFactory scopes)
    {
        _cache = cache;
        _scopes = scopes;
    }

    public async Task<SystemConfigSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out SystemConfigSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        // Provider là singleton còn repository gắn với DbContext theo request: mở một scope riêng cho lần đọc này.
        using var scope = _scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
        var row = await repository.GetActiveAsync(SystemConfigNames.App, ct);
        var snapshot = row is null
            ? new SystemConfigSnapshot(0, new SystemConfigDocument(), null)
            : new SystemConfigSnapshot(row.Version, JsonSerializer.Deserialize<SystemConfigDocument>(row.Content, Json) ?? new SystemConfigDocument(), row.CreatedAt);

        _cache.Set(CacheKey, snapshot, Ttl);
        return snapshot;
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
