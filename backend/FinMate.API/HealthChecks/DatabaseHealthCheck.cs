using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinMate.API.HealthChecks;

/// <summary>
/// Backend còn nối được PostgreSQL không.
///
/// Viết tay thay vì dùng <c>AspNetCore.HealthChecks.NpgSql</c>: đó là dependency mới, phải xin
/// duyệt theo AGENTS.md §5, trong khi thứ cần chỉ là một vòng gọi mà EF Core đã có sẵn.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly FinMateDbContext _context;

    public DatabaseHealthCheck(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL.");
        }
        catch (Exception ex)
        {
            // Nuốt và quy thành Unhealthy: health check mà ném thì trả 500 thay vì 503, và
            // 500 đọc như "app hỏng" chứ không phải "phụ thuộc của app hỏng".
            return HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL.", ex);
        }
    }
}
