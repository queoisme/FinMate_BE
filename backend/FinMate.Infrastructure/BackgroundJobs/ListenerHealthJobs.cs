using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.ListenerHealth;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// 09:00 VN mỗi ngày (BR-LH-05): nhắc từng user có ví đang theo dõi, ghi số ca <c>upload_gap</c> theo phiên bản app (S2), rồi dọn dòng theo ngày quá hạn (D20).
/// Một user lỗi không chặn user khác; bước dọn tách riêng để lỗi ở đó không chặn việc nhắc. Chạy lại trong ngày an toàn: nhắc chống lặp theo <c>dedup_key</c>,
/// số ca upload gap được thay chứ không cộng. Log chỉ có số đếm, không có tên ví hay package.
/// </summary>
public class ListenerHealthDailyJob
{
    private const int PageSize = 200;
    private const int PurgeBatchSize = 5_000;
    public const string UnknownAppVersion = "unknown";

    private readonly IListenerHealthRepository _repository;
    private readonly IListenerHealthReminderService _reminders;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<ListenerHealthDailyJob> _logger;

    public ListenerHealthDailyJob(
        IListenerHealthRepository repository, IListenerHealthReminderService reminders, ISystemConfigProvider config, ILogger<ListenerHealthDailyJob> logger)
    {
        _repository = repository;
        _reminders = reminders;
        _config = config;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public Task RunAsync(CancellationToken ct = default) => RunAtAsync(DateTimeOffset.UtcNow, ct);

    public async Task RunAtAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.ListenerHealth;
        var today = VietnamTime.DateOf(now);
        var gaps = new Dictionary<string, int>(StringComparer.Ordinal);
        int users = 0, reminded = 0, failed = 0;

        Guid? after = null;
        while (true)
        {
            var page = await _repository.ListWatchedUserIdsAsync(after, PageSize, ct);
            foreach (var userId in page)
            {
                users++;
                try
                {
                    var result = await _reminders.RunForUserAsync(userId, now, ct);
                    reminded += result.RemindersRaised;
                    foreach (var version in result.UploadGapAppVersions)
                    {
                        var key = string.IsNullOrWhiteSpace(version) ? UnknownAppVersion : version;
                        gaps[key] = gaps.GetValueOrDefault(key) + 1;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogError(ex, "Listener health check failed for user {UserId}", userId);
                }
            }

            if (page.Count < PageSize)
            {
                break;
            }

            after = page[^1];
        }

        try
        {
            await _repository.ReplaceUploadGapCountsAsync(today, gaps, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not save listener upload gap counts");
        }

        _logger.LogInformation(
            "Listener health: {Users} users checked, {Reminders} reminders, {UploadGaps} upload gaps, {Failed} failed",
            users, reminded, gaps.Values.Sum(), failed);

        try
        {
            var cutoff = today.AddDays(-settings.HistoryRetentionDays);
            int deleted;
            do
            {
                deleted = await _repository.PurgeDailyRowsBeforeAsync(cutoff, PurgeBatchSize, ct);
            }
            while (deleted > 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Listener health purge failed");
        }
    }
}
