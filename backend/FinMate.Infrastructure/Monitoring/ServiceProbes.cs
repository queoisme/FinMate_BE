using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Caching.Distributed;

namespace FinMate.Infrastructure.Monitoring;

/// <summary>Đọc một khóa không tồn tại: trả null vẫn là một vòng đi về trọn vẹn, và đi đúng đường ứng dụng dùng.</summary>
public sealed class RedisProbe : IServiceProbe
{
    private const string ProbeKey = "healthcheck:probe";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IDistributedCache _cache;

    public RedisProbe(IDistributedCache cache)
    {
        _cache = cache;
    }

    public MonitoredService Service => MonitoredServices.Redis;

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await _cache.GetAsync(ProbeKey, timeout.Token);
            return new ProbeResult(MonitoredServiceState.Healthy);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(MonitoredServiceState.Unhealthy, ex.GetType().Name);
        }
    }
}

/// <summary>Gọi <c>/health</c> của một dịch vụ nội bộ. Chi tiết chỉ có mã HTTP hoặc tên kiểu lỗi, không có nội dung phản hồi.</summary>
public sealed class HttpServiceProbe : IServiceProbe
{
    public const string HttpClientName = "health-probe";

    private readonly IHttpClientFactory _http;
    private readonly string? _healthUrl;

    public HttpServiceProbe(MonitoredService service, string? healthUrl, IHttpClientFactory http)
    {
        Service = service;
        _healthUrl = healthUrl;
        _http = http;
    }

    public MonitoredService Service { get; }

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_healthUrl))
        {
            return new ProbeResult(MonitoredServiceState.Disabled);
        }

        try
        {
            using var response = await _http.CreateClient(HttpClientName).GetAsync(_healthUrl, ct);
            return response.IsSuccessStatusCode
                ? new ProbeResult(MonitoredServiceState.Healthy)
                : new ProbeResult(MonitoredServiceState.Unhealthy, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(MonitoredServiceState.Unhealthy, ex.GetType().Name);
        }
    }
}
