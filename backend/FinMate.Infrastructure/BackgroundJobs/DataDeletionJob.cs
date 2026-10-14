using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Hard-deletes users whose 30-day retention window (set by DeleteAccountCommand) has passed.
/// This is the one explicit hard-delete case permitted by AGENTS.md §3.3.
/// </summary>
public class DataDeletionJob
{
    private readonly IDataDeletionRequestRepository _dataDeletionRequestRepository;
    private readonly IUserHardDeleter _userHardDeleter;
    private readonly ITrainingSampleService? _trainingSamples;

    public DataDeletionJob(
        IDataDeletionRequestRepository dataDeletionRequestRepository,
        IUserHardDeleter userHardDeleter,
        ITrainingSampleService? trainingSamples = null)
    {
        _dataDeletionRequestRepository = dataDeletionRequestRepository;
        _userHardDeleter = userHardDeleter;
        _trainingSamples = trainingSamples;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var due = await _dataDeletionRequestRepository.GetDueAsync(DateTimeOffset.UtcNow, ct);

        foreach (var request in due)
        {
            // Mẫu huấn luyện ở ai-service chỉ tìm được qua người dùng này: xóa TRƯỚC khi xóa tài khoản.
            // Chưa xóa được thì giữ nguyên người dùng và yêu cầu: đêm sau job chạy lại (xóa cứng xong là không còn cách tìm dữ liệu của họ).
            if (_trainingSamples is not null && !await _trainingSamples.EraseAsync(request.UserId, ct))
            {
                continue;
            }

            await _userHardDeleter.HardDeleteAsync(request.UserId, ct);
            await _dataDeletionRequestRepository.MarkProcessedAsync(request, ct);
        }
    }
}
