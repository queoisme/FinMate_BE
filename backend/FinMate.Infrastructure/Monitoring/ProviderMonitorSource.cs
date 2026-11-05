using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Monitoring;

/// <summary>
/// Nhà cung cấp bên ngoài. Đây là nguồn duy nhất phát hiện được key Vision hết hạn mức: khi đó OCR tự đọc cục bộ, health check vẫn xanh.
/// Bên gọi đã đếm lỗi liên tiếp, và số đếm chỉ về 0 khi có một lần gọi thành công thật, nên ở đây không cần đếm lại theo từng phút.
/// Chỉ coi là tốt khi đã thấy một lần gọi thành công. Dịch vụ vừa khởi động lại có số đếm bằng 0 nhưng chưa chứng minh gì, nên là chưa biết, và sự cố đang mở giữ nguyên.
/// </summary>
public sealed class ProviderMonitorSource : IMonitorSource
{
    private readonly IProviderStatusClient _client;
    private readonly IProviderFailureTracker _tracker;
    private readonly ServiceStatusTracker _status;

    public ProviderMonitorSource(IProviderStatusClient client, IProviderFailureTracker tracker, ServiceStatusTracker status)
    {
        _client = client;
        _tracker = tracker;
        _status = status;
    }

    public async Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        var threshold = settings.ProviderFailureThreshold;
        var ocr = await _client.GetOcrStatusAsync(ct);
        var ai = await _client.GetAiStatusAsync(ct);

        await ApplyAsync(MonitoredServices.Vision, Map(ocr?.Vision, threshold), now, ct);
        await ApplyAsync(MonitoredServices.Gemini, Map(ai?.Gemini, threshold), now, ct);
        await ApplyAsync(MonitoredServices.GeminiBackup, Map(ai?.GeminiBackup, threshold), now, ct);
        await ApplyAsync(MonitoredServices.Rasa, MapRasa(ai?.Rasa), now, ct);
        await ApplyAsync(MonitoredServices.Brevo, MapLocal(MonitoredServices.Brevo.Key, threshold), now, ct);
        await ApplyAsync(MonitoredServices.Fcm, MapLocal(MonitoredServices.Fcm.Key, threshold), now, ct);
    }

    private Task ApplyAsync(MonitoredService service, ProbeResult result, DateTimeOffset now, CancellationToken ct)
        => _status.ApplyAsync(service, IncidentSource.Provider, result, failureThreshold: 1, recoveryThreshold: 1, now, ct);

    internal static ProbeResult Map(ProviderHealthBody? body, int threshold)
    {
        if (body is null || body.State == "unknown")
        {
            return new ProbeResult(MonitoredServiceState.Unknown);
        }

        if (body.State == "disabled")
        {
            return new ProbeResult(MonitoredServiceState.Disabled);
        }

        if (body.ConsecutiveFailures >= threshold)
        {
            return new ProbeResult(MonitoredServiceState.Unhealthy, Detail(body.LastErrorCode, body.ConsecutiveFailures));
        }

        return body.ConsecutiveFailures == 0 && body.LastSuccessAt is not null
            ? new ProbeResult(MonitoredServiceState.Healthy)
            : new ProbeResult(MonitoredServiceState.Unknown);
    }

    internal static ProbeResult MapRasa(RasaStatusBody? rasa)
    {
        if (rasa is null)
        {
            return new ProbeResult(MonitoredServiceState.Unknown);
        }

        if (!rasa.Configured)
        {
            return new ProbeResult(MonitoredServiceState.Disabled);
        }

        return rasa.Available ? new ProbeResult(MonitoredServiceState.Healthy) : new ProbeResult(MonitoredServiceState.Unhealthy, "không phản hồi");
    }

    private ProbeResult MapLocal(string provider, int threshold)
    {
        var state = _tracker.Get(provider);
        if (state.NotConfigured)
        {
            return new ProbeResult(MonitoredServiceState.Disabled);
        }

        if (state.ConsecutiveFailures >= threshold)
        {
            return new ProbeResult(MonitoredServiceState.Unhealthy, Detail(state.LastErrorCode, state.ConsecutiveFailures));
        }

        return state.ConsecutiveFailures == 0 && state.HasSucceeded
            ? new ProbeResult(MonitoredServiceState.Healthy)
            : new ProbeResult(MonitoredServiceState.Unknown);
    }

    private static string Detail(string? code, int failures)
    {
        var detail = $"mã {code ?? "không rõ"}, lỗi liên tiếp {failures} lần";
        return detail.Length <= 200 ? detail : detail[..200];
    }
}
