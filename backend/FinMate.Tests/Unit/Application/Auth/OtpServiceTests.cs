using FinMate.Application.Auth.Otp;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

/// <summary>
/// Mã 6 chữ số chỉ có 10⁶ khả năng, nên thứ bảo vệ thật không phải phép băm mà là: TTL ngắn,
/// giới hạn số lần thử, và dùng một lần là huỷ. Mỗi test dưới đây ghim một trong ba thứ đó.
/// </summary>
public class OtpServiceTests
{
    /// <summary>Cache trong bộ nhớ, không TTL — đủ cho mọi thứ trừ phần hết hạn.</summary>
    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _entries = [];

        public int Count => _entries.Count;

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

        public void ClearCooldowns()
        {
            foreach (var key in _entries.Keys.Where(k => k.StartsWith("otp-cooldown:")).ToList())
            {
                _entries.Remove(key);
            }
        }
    }

    private readonly FakeCache _cache = new();
    private readonly OtpService _service;

    private const string Email = "nguoi.dung@finmate.test";

    public OtpServiceTests()
    {
        _service = new OtpService(_cache);
    }

    private async Task<string> IssueAsync(OtpPurpose purpose = OtpPurpose.VerifyEmail)
    {
        _cache.ClearCooldowns();
        var issued = await _service.IssueAsync(Email, purpose);
        issued.Issued.Should().BeTrue();
        return issued.Code!;
    }

    [Fact]
    public async Task ACorrectCodeIsAccepted()
    {
        var code = await IssueAsync();

        (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, code))
            .Should().Be(OtpVerifyResult.Valid);
    }

    [Fact]
    public async Task TheCodeIsSixDigits()
    {
        var code = await IssueAsync();

        code.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task ACodeWorksOnlyOnce()
    {
        // Không huỷ sau khi dùng thì mã còn sống tới hết TTL, và ai đọc được nó — qua log,
        // qua màn hình người dùng — vẫn dùng lại được.
        var code = await IssueAsync();

        await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, code);

        (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, code))
            .Should().Be(OtpVerifyResult.Invalid);
    }

    [Fact]
    public async Task GuessingIsCutOffAfterFiveTries()
    {
        var code = await IssueAsync();

        for (var i = 0; i < 5; i++)
        {
            (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, "000000"))
                .Should().Be(OtpVerifyResult.Invalid);
        }

        (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, "000000"))
            .Should().Be(OtpVerifyResult.TooManyAttempts);

        // Và mã thật cũng chết theo — nếu không, kẻ dò chỉ cần xin mã mới rồi dò tiếp trên mã cũ.
        (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, code))
            .Should().Be(OtpVerifyResult.Invalid);
    }

    [Fact]
    public async Task ACodeForOnePurposeDoesNotWorkForAnother()
    {
        // Thiếu tách biệt thì mã xin được qua "gửi lại mã xác minh" sẽ đặt lại được mật khẩu.
        var code = await IssueAsync(OtpPurpose.VerifyEmail);

        (await _service.VerifyAsync(Email, OtpPurpose.ResetPassword, code))
            .Should().Be(OtpVerifyResult.Invalid);
    }

    [Fact]
    public async Task ACodeForOneEmailDoesNotWorkForAnother()
    {
        var code = await IssueAsync();

        (await _service.VerifyAsync("nguoi.khac@finmate.test", OtpPurpose.VerifyEmail, code))
            .Should().Be(OtpVerifyResult.Invalid);
    }

    [Fact]
    public async Task VerifyingWithoutEverAskingIsJustInvalid()
    {
        (await _service.VerifyAsync(Email, OtpPurpose.VerifyEmail, "123456"))
            .Should().Be(OtpVerifyResult.Invalid);
    }

    [Fact]
    public async Task AskingAgainImmediatelyIsRefused()
    {
        // Hạn mức theo IP không chặn được việc nhắm vào MỘT hộp thư cụ thể, mà mỗi lần gửi vừa
        // tốn tiền vừa là rác trong hộp thư người khác.
        await _service.IssueAsync(Email, OtpPurpose.VerifyEmail);

        var second = await _service.IssueAsync(Email, OtpPurpose.VerifyEmail);

        second.Issued.Should().BeFalse();
        second.Code.Should().BeNull();
    }

    [Fact]
    public async Task TheCooldownIsPerPurpose()
    {
        await _service.IssueAsync(Email, OtpPurpose.VerifyEmail);

        // Vừa xin mã xác minh không được chặn mất đường quên mật khẩu — hai việc khác nhau.
        (await _service.IssueAsync(Email, OtpPurpose.ResetPassword)).Issued.Should().BeTrue();
    }

    [Fact]
    public async Task TheCodeItselfIsNeverStored()
    {
        // Băm không cứu được ai đã có dump (10⁶ khả năng dò trong vài giây), nhưng mã nằm
        // nguyên dạng trong dump hay log thì còn tệ hơn hẳn.
        var code = await IssueAsync();

        var stored = await _cache.GetAsync<object>(
            _cache.Count > 0 ? "otp:VerifyEmail:" + Sha256(Email) : "");

        System.Text.Json.JsonSerializer.Serialize(stored).Should().NotContain(code);
    }

    private static string Sha256(string value)
        => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
