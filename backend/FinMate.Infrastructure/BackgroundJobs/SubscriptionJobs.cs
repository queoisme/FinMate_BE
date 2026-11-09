using FinMate.Application.Subscriptions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>Lưới an toàn cho IPN: tài liệu SePay không nói IPN có gửi lại hay không, nên tiền đã về phải được ghi nhận kể cả khi IPN mất.</summary>
public class SubscriptionReconcileJob
{
    private readonly ISubscriptionSettlementService _settlement;
    private readonly ILogger<SubscriptionReconcileJob> _logger;

    public SubscriptionReconcileJob(ISubscriptionSettlementService settlement, ILogger<SubscriptionReconcileJob> logger)
    {
        _settlement = settlement;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(CancellationToken ct)
    {
        var settled = await _settlement.ReconcileAsync(DateTimeOffset.UtcNow, ct);
        if (settled > 0)
        {
            _logger.LogInformation("Đối soát ghi nhận {Count} đơn đăng ký mà IPN chưa về", settled);
        }
    }
}

public class SubscriptionReminderJob
{
    private readonly ISubscriptionSettlementService _settlement;
    private readonly ILogger<SubscriptionReminderJob> _logger;

    public SubscriptionReminderJob(ISubscriptionSettlementService settlement, ILogger<SubscriptionReminderJob> logger)
    {
        _settlement = settlement;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public async Task RunAsync(CancellationToken ct)
    {
        var sent = await _settlement.SendExpiryRemindersAsync(DateTimeOffset.UtcNow, ct);
        _logger.LogInformation("Đã nhắc {Count} gói đăng ký sắp hết hạn", sent);
    }
}
