using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Loans;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// 08:05 VN mỗi ngày (Phase 90, BR-DEBT-14, Q10; thay <c>LoanDueReminderJob</c>): nhắc khoản vay sắp đến hạn và quá hạn, qua hộp thư <c>forecast_alerts</c>.
/// Chỉ xét user có khoản <c>open</c> hạn tới hết <c>hôm nay + reminderDaysBefore</c>. Một user lỗi không chặn user khác. Chạy lại trong ngày an toàn:
/// nhắc chống lặp theo <c>dedup_key</c>. Log chỉ có số đếm, không có tên người hay số tiền.
/// </summary>
public class LoanReminderJob(ILoanRepository loans, ILoanReminderService reminders, ISystemConfigProvider config, ILogger<LoanReminderJob> logger)
{
    public const string JobId = "loan-reminders";
    public const string RetiredJobId = "loan-due-reminder";

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public Task RunAsync(CancellationToken ct = default) => RunAtAsync(DateTimeOffset.UtcNow, ct);

    public async Task<int> RunAtAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await config.GetAsync(ct)).Document.Loans;
        var until = VietnamTime.DateOf(now).AddDays(settings.ReminderDaysBefore);
        int users = 0, reminded = 0, failed = 0;
        foreach (var userId in await loans.ListUserIdsWithOpenLoansDueByAsync(until, ct))
        {
            users++;
            try
            {
                reminded += await reminders.RunForUserAsync(userId, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Loan reminders failed for user {UserId}", userId);
            }
        }

        logger.LogInformation("Loan reminders: {Users} users, {Reminded} reminders, {Failed} failed", users, reminded, failed);
        return reminded;
    }
}
