using System.Collections.Concurrent;

namespace FinMate.Application.Monitoring;

public sealed record ProviderFailureState(int ConsecutiveFailures, string? LastErrorCode);

/// <summary>Đếm lỗi liên tiếp của nhà cung cấp do chính backend gọi (Brevo, FCM). Trong bộ nhớ của tiến trình; khởi động lại thì đếm lại từ đầu, lỗi thật sẽ lặp lại. Chỉ giữ mã lỗi, không giữ khóa, người nhận hay nội dung.</summary>
public interface IProviderFailureTracker
{
    void RecordSuccess(string provider);
    void RecordFailure(string provider, string code);
    ProviderFailureState Get(string provider);
}

public sealed class ProviderFailureTracker : IProviderFailureTracker
{
    private readonly ConcurrentDictionary<string, ProviderFailureState> _states = new();

    public void RecordSuccess(string provider) => _states[provider] = new ProviderFailureState(0, null);

    public void RecordFailure(string provider, string code)
        => _states.AddOrUpdate(
            provider,
            _ => new ProviderFailureState(1, code),
            (_, current) => new ProviderFailureState(current.ConsecutiveFailures + 1, code));

    public ProviderFailureState Get(string provider)
        => _states.TryGetValue(provider, out var state) ? state : new ProviderFailureState(0, null);
}
