using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Subscriptions;

public interface IBillingWebSessionStore
{
    Task<string> IssueHandoffAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> ConsumeHandoffAsync(string code, CancellationToken ct = default);
    Task<string> IssueSessionAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetSessionUserAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Website thanh toán không bao giờ cầm JWT. Mã trên URL có thể vào lịch sử trình duyệt nên sống ngắn và dùng một lần; phiên đổi được chỉ mở các API thanh toán,
/// không đọc được dữ liệu tài chính. Chỉ lưu SHA-256, nên bản sao Redis không dùng được để giả phiên.
/// </summary>
public sealed class BillingWebSessionStore : IBillingWebSessionStore
{
    public static readonly TimeSpan HandoffTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    private readonly ICacheService _cache;

    public BillingWebSessionStore(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<string> IssueHandoffAsync(Guid userId, CancellationToken ct = default)
    {
        var code = NewToken();
        await _cache.SetAsync(HandoffKey(code), userId.ToString(), HandoffTtl, ct);
        return code;
    }

    public async Task<Guid?> ConsumeHandoffAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var key = HandoffKey(code);
        var value = await _cache.GetAsync<string>(key, ct);
        if (value is null)
        {
            return null;
        }

        await _cache.RemoveAsync(key, ct);
        return Guid.Parse(value);
    }

    public async Task<string> IssueSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var token = NewToken();
        await _cache.SetAsync(SessionKey(token), userId.ToString(), SessionTtl, ct);
        return token;
    }

    public async Task<Guid?> GetSessionUserAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var value = await _cache.GetAsync<string>(SessionKey(token), ct);
        return value is null ? null : Guid.Parse(value);
    }

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HandoffKey(string code) => $"billing-handoff:{Hash(code)}";

    private static string SessionKey(string token) => $"billing-session:{Hash(token)}";
}
