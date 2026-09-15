using System.Security.Cryptography;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.GoogleOAuth;

public class GoogleOAuthStore : IGoogleOAuthStore
{
    /// <summary>Đủ thời gian cho người dùng chọn tài khoản và gõ mật khẩu Google.</summary>
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Rất ngắn có chủ ý: app chỉ cần đủ thời gian để nhận deep link rồi gọi đổi mã ngay.
    /// Mã này thay mặt cho cả access token lẫn refresh token, nên sống càng ngắn càng tốt.
    /// </summary>
    private static readonly TimeSpan HandoffTtl = TimeSpan.FromMinutes(2);

    private readonly ICacheService _cache;

    public GoogleOAuthStore(ICacheService cache)
    {
        _cache = cache;
    }

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static string StateKey(string state) => $"google-oauth-state:{state}";

    private static string HandoffKey(string code) => $"google-oauth-handoff:{code}";

    public async Task<string> IssueStateAsync(CancellationToken ct = default)
    {
        var state = NewToken();
        await _cache.SetAsync(StateKey(state), "1", StateTtl, ct);
        return state;
    }

    public async Task<bool> ConsumeStateAsync(string state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        var key = StateKey(state);
        if (await _cache.GetAsync<string>(key, ct) is null)
        {
            return false;
        }

        // Huỷ ngay: cho dùng lại là mở đường phát lại nguyên một lần callback.
        await _cache.RemoveAsync(key, ct);
        return true;
    }

    public async Task<string> IssueHandoffAsync(AuthResultDto result, CancellationToken ct = default)
    {
        var code = NewToken();
        await _cache.SetAsync(HandoffKey(code), result, HandoffTtl, ct);
        return code;
    }

    public async Task<AuthResultDto?> ConsumeHandoffAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var key = HandoffKey(code);
        var result = await _cache.GetAsync<AuthResultDto>(key, ct);
        if (result is null)
        {
            return null;
        }

        await _cache.RemoveAsync(key, ct);
        return result;
    }
}
