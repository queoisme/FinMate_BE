using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class AlertThresholdProvider : IAlertThresholdProvider
{
    private readonly FinMateDbContext _context;
    private readonly ISystemConfigProvider _config;

    public AlertThresholdProvider(FinMateDbContext context, ISystemConfigProvider config)
    {
        _context = context;
        _config = config;
    }

    public async Task<AlertThresholdResolution> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        // Sắp theo role: 'primary' đứng trước 'supporting' (so sánh chuỗi), nên cách chính thắng khi cả hai đều định nghĩa T1/T2.
        var methods = await _context.UserMethods
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == UserMethodStatus.Active)
            .OrderBy(m => m.Role)
            .Join(_context.FinanceMethods, m => m.MethodId, f => f.Id, (m, f) => new { m.Params, f.ParamDefs, f.Code, f.Name })
            .ToListAsync(ct);

        foreach (var method in methods)
        {
            var thresholds = AlertThresholds.FromMethod(method.ParamDefs, method.Params);
            if (thresholds is not null)
            {
                return new AlertThresholdResolution(thresholds, AlertThresholdSource.Method, method.Code, method.Name);
            }
        }

        // Không cách nào định nghĩa T1/T2: ngưỡng cá nhân (nếu hợp lệ), rồi mặc định hệ thống.
        var personal = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.NotificationPrefs.AlertT1Pct, u.NotificationPrefs.AlertT2Pct })
            .FirstOrDefaultAsync(ct);
        if (personal is { AlertT1Pct: { } t1, AlertT2Pct: { } t2 } && AlertThresholds.IsValid(new AlertThresholds(t1, t2)))
        {
            return new AlertThresholdResolution(new AlertThresholds(t1, t2), AlertThresholdSource.Personal);
        }

        return new AlertThresholdResolution(await GetSystemDefaultAsync(ct), AlertThresholdSource.System);
    }

    public async Task<AlertThresholds> GetAsync(Guid userId, CancellationToken ct = default) => (await ResolveAsync(userId, ct)).Thresholds;

    public async Task<AlertThresholds> GetSystemDefaultAsync(CancellationToken ct = default)
    {
        try
        {
            var limits = (await _config.GetAsync(ct)).Document.Limits;
            var configured = new AlertThresholds(limits.AlertT1Percent, limits.AlertT2Percent);
            return AlertThresholds.IsValid(configured) ? configured : AlertThresholds.Default;
        }
        catch (Exception)
        {
            // Đường cảnh báo chạy sau khi giao dịch đã lưu: không đọc được cấu hình thì dùng phao cứu sinh, không được làm hỏng giao dịch.
            return AlertThresholds.Default;
        }
    }
}
