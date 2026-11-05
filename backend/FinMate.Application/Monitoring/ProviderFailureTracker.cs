using System.Collections.Concurrent;

namespace FinMate.Application.Monitoring;

/// <param name="HasSucceeded">Đã có lần gọi thành công từ khi tiến trình chạy. Chưa có thì không thể nói nhà cung cấp đang tốt.</param>
/// <param name="NotConfigured">Lần gửi gần nhất không có cấu hình nên không gọi nhà cung cấp.</param>
public sealed record ProviderFailureState(int ConsecutiveFailures, string? LastErrorCode, bool HasSucceeded = false, bool NotConfigured = false);

/// <summary>Đếm lỗi liên tiếp của nhà cung cấp do chính backend gọi (Brevo, FCM). Trong bộ nhớ của tiến trình; khởi động lại thì đếm lại từ đầu, lỗi thật sẽ lặp lại. Chỉ giữ mã lỗi, không giữ khóa, người nhận hay nội dung.</summary>
public interface IProviderFailureTracker
{
    void RecordSuccess(string provider);
    void RecordFailure(string provider, string code);
    void RecordNotConfigured(string provider);
    ProviderFailureState Get(string provider);
}

public sealed class ProviderFailureTracker : IProviderFailureTracker
{
    private readonly ConcurrentDictionary<string, ProviderFailureState> _states = new();

    public void RecordSuccess(string provider) => _states[provider] = new ProviderFailureState(0, null, HasSucceeded: true);

    public void RecordNotConfigured(string provider) => _states[provider] = new ProviderFailureState(0, null, NotConfigured: true);

    public void RecordFailure(string provider, string code)
        => _states.AddOrUpdate(
            provider,
            _ => new ProviderFailureState(1, code),
            (_, current) => new ProviderFailureState(current.ConsecutiveFailures + 1, code, current.HasSucceeded));

    public ProviderFailureState Get(string provider)
        => _states.TryGetValue(provider, out var state) ? state : new ProviderFailureState(0, null);
}
