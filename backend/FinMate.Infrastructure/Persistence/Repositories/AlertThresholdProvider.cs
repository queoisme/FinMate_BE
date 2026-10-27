using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class AlertThresholdProvider : IAlertThresholdProvider
{
    private readonly FinMateDbContext _context;

    public AlertThresholdProvider(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<AlertThresholds> GetAsync(Guid userId, CancellationToken ct = default)
    {
        // Sắp theo role: 'primary' đứng trước 'supporting' (so sánh chuỗi), nên cách chính thắng khi cả hai đều định nghĩa T1/T2.
        var methods = await _context.UserMethods
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == UserMethodStatus.Active)
            .OrderBy(m => m.Role)
            .Join(_context.FinanceMethods, m => m.MethodId, f => f.Id, (m, f) => new { m.Params, f.ParamDefs })
            .ToListAsync(ct);

        foreach (var method in methods)
        {
            var thresholds = AlertThresholds.FromMethod(method.ParamDefs, method.Params);
            if (thresholds is not null)
            {
                return thresholds;
            }
        }

        return AlertThresholds.Default;
    }
}
