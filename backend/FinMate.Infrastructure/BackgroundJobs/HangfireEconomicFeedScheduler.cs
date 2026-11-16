using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Economics;
using Hangfire;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Đăng ký (hoặc gỡ) job làm mới CPI theo ngày và giờ trong cấu hình hệ thống. Admin nhập giờ Việt Nam (UTC+7, không đổi giờ mùa hè); Hangfire chạy cron theo UTC
/// nên ở đây đổi sang UTC, thay vì dùng múi giờ của hệ điều hành (container thiếu dữ liệu múi giờ sẽ làm khởi động thất bại).
/// Hangfire lưu lịch trong Postgres: một máy đăng ký là mọi máy thấy.
/// </summary>
public sealed class HangfireEconomicFeedScheduler(IRecurringJobManager jobs) : IEconomicFeedScheduler
{
    public const string JobId = "economic-assumption-refresh";

    private const int VietnamUtcOffsetHours = 7;

    public static string Cron(EconomicFeedsSettings settings)
    {
        var hour = settings.ScheduleHour - VietnamUtcOffsetHours;
        var day = settings.ScheduleDayOfMonth;
        if (hour < 0)
        {
            hour += 24;
            day--;   // giờ sớm ở Việt Nam là hôm trước theo UTC
        }

        return $"0 {hour} {(day == 0 ? "L" : day.ToString())} * *";   // "L" = ngày cuối tháng (ngày 1 giờ sớm rơi vào cuối tháng trước)
    }

    public void Apply(EconomicFeedsSettings settings)
    {
        if (!settings.Enabled)
        {
            jobs.RemoveIfExists(JobId);
            return;
        }

        jobs.AddOrUpdate<EconomicAssumptionRefreshJob>(JobId, job => job.RunAsync(CancellationToken.None), Cron(settings));
    }
}
