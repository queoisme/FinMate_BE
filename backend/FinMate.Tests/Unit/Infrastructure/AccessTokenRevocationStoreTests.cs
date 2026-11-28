using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class AccessTokenRevocationStoreTests
{
    private readonly FakeCache _cache = new();
    private readonly FixedClock _clock = new(DateTimeOffset.FromUnixTimeMilliseconds(100_500)); // 100,5 s
    private readonly Guid _user = Guid.NewGuid();

    private AccessTokenRevocationStore Store(ICacheService? cache = null) => new(
        cache ?? _cache,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JWT_ACCESS_TTL_MINUTES"] = "15" }).Build(),
        NullLogger<AccessTokenRevocationStore>.Instance,
        _clock);

    [Fact]
    public async Task NothingIsRevokedUntilSomeoneRevokes()
    {
        (await Store().IsRevokedAsync(_user, 100)).Should().BeFalse();
        (await Store().IsRevokedAsync(_user, null)).Should().BeFalse();
    }

    [Fact]
    public async Task TokensIssuedUpToTheRevocationSecondAreRevoked_LaterOnesAreNot()
    {
        // Mốc là giây NGUYÊN kế tiếp (101): iat tính bằng giây nên token cấp ở giây 100, kể cả
        // trước hay sau lệnh thu hồi trong cùng giây đó, đều bị thu hồi; token cấp từ giây 101 thì không.
        await Store().RevokeAllAsync(_user);

        (await Store().IsRevokedAsync(_user, 100)).Should().BeTrue();
        (await Store().IsRevokedAsync(_user, 50)).Should().BeTrue();
        (await Store().IsRevokedAsync(_user, 101)).Should().BeFalse();
    }

    [Fact]
    public async Task ATokenWithoutIssuedAtIsRevokedOnceAMarkExists()
    {
        // Token cấp trước khi có claim iat không so được với mốc; thà thu hồi (còn sống tối đa một TTL) hơn để lọt.
        await Store().RevokeAllAsync(_user);

        (await Store().IsRevokedAsync(_user, null)).Should().BeTrue();
    }

    [Fact]
    public async Task RevocationOfOneUserDoesNotAffectAnother()
    {
        await Store().RevokeAllAsync(_user);

        (await Store().IsRevokedAsync(Guid.NewGuid(), 100)).Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheCacheIsDownCheckingFailsOpenAndRevokingDoesNotThrow()
    {
        // Redis sập không được khóa cả hệ thống; DB vẫn là nguồn sự thật (refresh token đã bị thu hồi, lần refresh sau bị chặn).
        var broken = new FakeCache { Throw = true };

        (await Store(broken).IsRevokedAsync(_user, 100)).Should().BeFalse();
        var act = () => Store(broken).RevokeAllAsync(_user);
        await act.Should().NotThrowAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCache : ICacheService
    {
        public bool Throw { get; init; }
        private readonly Dictionary<string, string> _items = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("redis down");
            return Task.FromResult(_items.TryGetValue(key, out var json)
                ? System.Text.Json.JsonSerializer.Deserialize<T>(json)
                : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("redis down");
            _items[key] = System.Text.Json.JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _items.Remove(key);
            return Task.CompletedTask;
        }
    }
}
