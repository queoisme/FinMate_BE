using FinMate.Application.DataExport;
using Hangfire;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Dựng một gói dữ liệu cá nhân (Phase 91). Không để Hangfire tự thử lại: runner tự đếm lượt và job quét chạy lại.</summary>
public sealed class PersonalDataExportJob(PersonalDataExportRunner runner)
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid exportId, CancellationToken ct) => runner.RunAsync(exportId, ct);
}

/// <summary>Mỗi 5 phút: chạy lại yêu cầu bị mất khi enqueue hoặc bỏ dở khi job chết.</summary>
public sealed class PersonalDataExportSweepJob(PersonalDataExportRunner runner)
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken ct) => runner.SweepAsync(ct);
}

/// <summary>Mỗi giờ: gói quá hạn giữ → Expired và xoá file; xoá lịch sử cũ.</summary>
public sealed class PersonalDataExportCleanupJob(PersonalDataExportCleanup cleanup)
{
    public Task RunAsync(CancellationToken ct) => cleanup.RunAsync(ct);
}

public sealed class HangfirePersonalDataExportQueue(IBackgroundJobClient jobs) : IPersonalDataExportQueue
{
    public void Enqueue(Guid exportId) => jobs.Enqueue<PersonalDataExportJob>(job => job.RunAsync(exportId, CancellationToken.None));
}
