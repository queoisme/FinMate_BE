using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<DataDeletionJob>? _logger;

    public DataDeletionJob(
        IDataDeletionRequestRepository dataDeletionRequestRepository,
        IUserHardDeleter userHardDeleter,
        ITrainingSampleService? trainingSamples = null,
        ILogger<DataDeletionJob>? logger = null)
    {
        _dataDeletionRequestRepository = dataDeletionRequestRepository;
        _userHardDeleter = userHardDeleter;
        _trainingSamples = trainingSamples;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var due = await _dataDeletionRequestRepository.GetDueAsync(DateTimeOffset.UtcNow, ct);

        foreach (var request in due)
        {
            // Lỗi của một người không được chặn yêu cầu xóa của những người sau: yêu cầu này vẫn chưa xử lý nên đêm sau job thử lại.
            try
            {
                await ProcessAsync(request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger?.LogError("Hard delete failed for deletion request {RequestId}: {ExceptionType}", request.Id, ex.GetType().Name);
            }
        }
    }

    private async Task ProcessAsync(DataDeletionRequest request, CancellationToken ct)
    {
        // Mẫu huấn luyện ở ai-service chỉ tìm được qua người dùng này: xóa TRƯỚC khi xóa tài khoản.
        // Chưa xóa được thì giữ nguyên người dùng và yêu cầu: đêm sau job chạy lại (xóa cứng xong là không còn cách tìm dữ liệu của họ)
        if (_trainingSamples is not null && !await _trainingSamples.EraseAsync(request.UserId, ct))
        {
            return;
        }

        await _userHardDeleter.HardDeleteAsync(request.UserId, ct);
        await _dataDeletionRequestRepository.MarkProcessedAsync(request, ct);
    }
}
