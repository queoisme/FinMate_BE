using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;

namespace FinMate.Infrastructure.Monitoring;

public sealed record RecurringJobInfo(string Id, string? LastJobState, DateTimeOffset? NextExecution);

public interface IRecurringJobReader
{
    IReadOnlyList<RecurringJobInfo> ReadAll();
}

public sealed class HangfireRecurringJobReader : IRecurringJobReader
{
    public IReadOnlyList<RecurringJobInfo> ReadAll()
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs()
            .Select(j => new RecurringJobInfo(j.Id, j.LastJobState, j.NextExecution is { } next ? new DateTimeOffset(next, TimeSpan.Zero) : null))
            .ToList();
    }
}

/// <summary>
/// Dựa vào trạng thái cuối của job, không dựa vào từng lần lỗi: một job đang được tự thử lại chưa phải là sự cố.
/// Job quá giờ chỉ ở mức thường, vì Hangfire chạy chậm một lúc rồi tự bắt kịp là chuyện bình thường.
/// </summary>
public sealed class JobMonitorSource : IMonitorSource
{
    private const string MonitorJobId = "system-monitor";
    private static readonly TimeSpan OverdueAfter = TimeSpan.FromMinutes(10);

    private readonly IRecurringJobReader _jobs;
    private readonly IIncidentService _incidents;
    private readonly IMonitoringRepository _repository;

    public JobMonitorSource(IRecurringJobReader jobs, IIncidentService incidents, IMonitoringRepository repository)
    {
        _repository = repository;
        _jobs = jobs;
        _incidents = incidents;
    }

    public async Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        var jobs = _jobs.ReadAll().Where(j => j.Id != MonitorJobId).ToList();

        // Job đã bị gỡ khỏi lịch thì không còn lần chạy nào để tự đóng, và sẽ nhắc mãi nếu không đóng ở đây.
        var currentKeys = jobs.SelectMany(j => new[] { $"job:failed:{j.Id}", $"job:overdue:{j.Id}" }).ToHashSet();
        foreach (var fingerprint in await _repository.ListActiveFingerprintsAsync(IncidentSource.Job, ct))
        {
            if (!currentKeys.Contains(fingerprint))
            {
                await _incidents.ResolveAsync(fingerprint, now, ct);
            }
        }

        foreach (var job in jobs)
        {
            var failedKey = $"job:failed:{job.Id}";
            if (job.LastJobState == FailedState.StateName)
            {
                await _incidents.RaiseAsync(
                    new IncidentSignal(failedKey, IncidentSource.Job, $"job:{job.Id}", IncidentSeverity.High, $"Tác vụ nền {job.Id} thất bại", job.LastJobState),
                    occurrences: 1,
                    now,
                    ct);
            }
            else
            {
                await _incidents.ResolveAsync(failedKey, now, ct);
            }

            var overdueKey = $"job:overdue:{job.Id}";
            if (job.NextExecution is { } next && now - next > OverdueAfter)
            {
                await _incidents.RaiseAsync(
                    new IncidentSignal(overdueKey, IncidentSource.Job, $"job:{job.Id}", IncidentSeverity.Normal, $"Tác vụ nền {job.Id} quá giờ chưa chạy", null),
                    occurrences: 1,
                    now,
                    ct);
            }
            else
            {
                await _incidents.ResolveAsync(overdueKey, now, ct);
            }
        }
    }
}
