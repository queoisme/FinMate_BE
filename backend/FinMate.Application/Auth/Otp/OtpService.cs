using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Otp;

public class OtpService : IOtpService
{
    /// <summary>Đủ ngắn để gõ tay, đủ dài để không đoán trúng trong 5 lần thử.</summary>
    private const int CodeDigits = 6;

    /// <summary>
    /// Ngắn có chủ ý: đây mới là thứ bảo vệ thật, không phải phép băm. Xem ghi chú ở
    /// <see cref="Hash"/>.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private const int MaxAttempts = 5;

    private readonly ICacheService _cache;

    public OtpService(ICacheService cache)
    {
        _cache = cache;
    }

    private sealed record StoredOtp(string CodeHash, int Attempts, DateTimeOffset IssuedAt);

    /// <summary>
    /// Khoá băm từ email chứ không dùng email thô: khoá Redis lọt vào log hay màn hình giám
    /// sát là lộ luôn danh sách ai đang đăng ký.
    /// </summary>
    private static string Key(string email, OtpPurpose purpose)
        => $"otp:{purpose}:{Hash(email.Trim().ToLowerInvariant())}";

    private static string CooldownKey(string email, OtpPurpose purpose)
        => $"otp-cooldown:{purpose}:{Hash(email.Trim().ToLowerInvariant())}";

    /// <summary>
    /// SHA-256, KHÔNG phải BCrypt. Nói thẳng: mã 6 chữ số chỉ có 10⁶ khả năng nên ai cầm được
    /// dump Redis sẽ dò ngược trong vài giây dù băm bằng gì. Băm ở đây chỉ để mã không nằm
    /// nguyên dạng trong dump hay log — thứ bảo vệ thật là TTL 10 phút, giới hạn 5 lần thử, và
    /// dùng một lần là huỷ. Dùng BCrypt chỉ làm mỗi lần kiểm chậm đi mà không đổi được điều đó.
    /// </summary>
    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<OtpIssueResult> IssueAsync(
        string email, OtpPurpose purpose, CancellationToken ct = default)
    {
        var cooldownKey = CooldownKey(email, purpose);
        if (await _cache.GetAsync<string>(cooldownKey, ct) is not null)
        {
            return new OtpIssueResult(Issued: false, Code: null);
        }

        // RandomNumberGenerator chứ không phải Random: mã đoán được là mã vô dụng.
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeDigits))
            .ToString($"D{CodeDigits}");

        await _cache.SetAsync(
            Key(email, purpose),
            new StoredOtp(Hash(code), Attempts: 0, IssuedAt: DateTimeOffset.UtcNow),
            Ttl,
            ct);

        await _cache.SetAsync(cooldownKey, "1", ResendCooldown, ct);

        return new OtpIssueResult(Issued: true, Code: code);
    }

    public async Task<OtpVerifyResult> VerifyAsync(
        string email, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var key = Key(email, purpose);
        var stored = await _cache.GetAsync<StoredOtp>(key, ct);

        if (stored is null)
        {
            // Không phân biệt "chưa từng gửi" với "đã hết hạn": nói ra là cho biết email nào
            // vừa xin mã.
            return OtpVerifyResult.Invalid;
        }

        if (stored.Attempts >= MaxAttempts)
        {
            await _cache.RemoveAsync(key, ct);
            return OtpVerifyResult.TooManyAttempts;
        }

        // So sánh theo thời gian hằng định. Rò rỉ qua thời gian trên một mã 6 chữ số sống 10
        // phút gần như không khai thác được, nhưng đây là giá 0 đồng.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(code)), Encoding.UTF8.GetBytes(stored.CodeHash)))
        {
            // Đọc-sửa-ghi, KHÔNG nguyên tử: ICacheService bọc IDistributedCache nên không có
            // INCR. Vài lần thử dư trong một cú bắn song song là chấp nhận được trên không
            // gian 10⁶, và rate limit theo IP vẫn áp ở trên.
            await _cache.SetAsync(key, stored with { Attempts = stored.Attempts + 1 }, Ttl, ct);
            return OtpVerifyResult.Invalid;
        }

        // Dùng một lần. Không xoá là mã còn sống tới hết TTL và ai đọc được nó vẫn dùng lại được.
        await _cache.RemoveAsync(key, ct);
        return OtpVerifyResult.Valid;
    }
}
