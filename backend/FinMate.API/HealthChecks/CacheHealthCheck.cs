using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinMate.API.HealthChecks;

/// <summary>
/// Backend còn nối được Redis không.
///
/// Đọc một khoá chắc chắn không tồn tại: trả về null vẫn là một vòng đi-về trọn vẹn, đủ chứng
/// minh kết nối sống, mà không ghi gì vào cache thật.
///
/// Đi qua <see cref="IDistributedCache"/> chứ không mở kết nối Redis riêng, nên nó kiểm đúng
/// cái đường mà ứng dụng thật sự dùng — một health check nối bằng đường khác có thể xanh trong
/// khi đường thật đã hỏng.
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private const string ProbeKey = "healthcheck:probe";

    private readonly IDistributedCache _cache;

    public CacheHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.GetAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được Redis.", ex);
        }
    }
}
