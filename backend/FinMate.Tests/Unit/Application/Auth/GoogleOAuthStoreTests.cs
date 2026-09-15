using FinMate.Application.Auth.GoogleOAuth;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class GoogleOAuthStoreTests
{
    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _entries = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(_entries.TryGetValue(key, out var v) ? (T?)v : default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _entries.Remove(key);
            return Task.CompletedTask;
        }
    }

    private readonly GoogleOAuthStore _store = new(new FakeCache());

    private static AuthResultDto SomeResult()
        => new("access-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(15));

    [Fact]
    public async Task AStateIsAcceptedExactlyOnce()
    {
        // Cho dùng lại là mở đường phát lại nguyên một lần callback.
        var state = await _store.IssueStateAsync();

        (await _store.ConsumeStateAsync(state)).Should().BeTrue();
        (await _store.ConsumeStateAsync(state)).Should().BeFalse();
    }

    [Fact]
    public async Task AStateNobodyIssuedIsRejected()
    {
        // Chốt chặn chính: thiếu nó thì kẻ tấn công dựng được callback bằng code của CHÚNG,
        // khiến nạn nhân đăng nhập vào tài khoản Google của kẻ tấn công.
        (await _store.ConsumeStateAsync("tu-bia-ra")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyStateIsRejected(string state)
    {
        (await _store.ConsumeStateAsync(state)).Should().BeFalse();
    }

    [Fact]
    public async Task AHandoffCodeReturnsTheTokensExactlyOnce()
    {
        var result = SomeResult();
        var code = await _store.IssueHandoffAsync(result);

        (await _store.ConsumeHandoffAsync(code)).Should().BeEquivalentTo(result);
        (await _store.ConsumeHandoffAsync(code)).Should().BeNull();
    }

    [Fact]
    public async Task AnUnknownHandoffCodeYieldsNothing()
    {
        (await _store.ConsumeHandoffAsync("khong-co-that")).Should().BeNull();
    }

    [Fact]
    public async Task IssuedValuesAreNotGuessable()
    {
        // Mã bàn giao thay mặt cho cả access token lẫn refresh token trong 2 phút.
        var codes = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            codes.Add(await _store.IssueHandoffAsync(SomeResult()));
        }

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length >= 32);
    }
}
