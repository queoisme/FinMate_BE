using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Hard-deletes users whose 30-day retention window (set by DeleteAccountCommand) has passed.
/// This is the one explicit hard-delete case permitted by AGENTS.md §3.3.
/// </summary>
public class DataDeletionJob
{
    private readonly IDataDeletionRequestRepository _dataDeletionRequestRepository;
    private readonly IUserHardDeleter _userHardDeleter;

    public DataDeletionJob(
        IDataDeletionRequestRepository dataDeletionRequestRepository,
        IUserHardDeleter userHardDeleter)
    {
        _dataDeletionRequestRepository = dataDeletionRequestRepository;
        _userHardDeleter = userHardDeleter;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var due = await _dataDeletionRequestRepository.GetDueAsync(DateTimeOffset.UtcNow, ct);

        foreach (var request in due)
        {
            await _userHardDeleter.HardDeleteAsync(request.UserId, ct);
            await _dataDeletionRequestRepository.MarkProcessedAsync(request, ct);
        }
    }
}
