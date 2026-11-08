using FinMate.Domain.Enums;
using Hangfire.Common;
using Hangfire.States;

namespace FinMate.Infrastructure.Monitoring;

/// <summary>Ghi nhận mỗi lần job lỗi, kể cả các lần sẽ được thử lại. Việc báo động dựa vào trạng thái cuối cùng của job, xem <c>JobMonitorSource</c>.</summary>
public sealed class OpsJobFailureFilter : JobFilterAttribute, IElectStateFilter
{
    private readonly OpsErrorQueue _queue;

    public OpsJobFailureFilter(OpsErrorQueue queue)
    {
        _queue = queue;

        // Trước AutomaticRetry, vì nó đổi trạng thái Failed thành Scheduled khi còn lượt thử.
        Order = 0;
    }

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is FailedState failed)
        {
            var jobType = context.BackgroundJob?.Job?.Type?.Name ?? "unknown";
            _queue.Enqueue(OpsErrorFactory.Create(
                $"job:{jobType}",
                IncidentSource.Job,
                failed.Exception,
                jobType,
                null,
                null,
                null,
                DateTimeOffset.UtcNow));
        }
    }
}
