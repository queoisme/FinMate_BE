using System.Collections.Concurrent;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Đồng hồ chỉnh tay cho test xuất dữ liệu.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Cache trong bộ nhớ, tuần tự hoá JSON như Redis thật (bỏ qua TTL).</summary>
internal sealed class JsonMapCache : ICacheService
{
    private readonly ConcurrentDictionary<string, string> _items = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        _items[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _items.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
