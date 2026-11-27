using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Phase 92 (S3): chạy tracker cho một user ngay sau khi giao dịch của họ được xác nhận (xếp hàng bởi <see cref="HangfireRecurringTrackingTrigger"/>).
/// Gỡ dấu "đã xếp hàng" TRƯỚC khi chạy, để giao dịch đến trong lúc job chạy xếp được lượt mới; hai lượt cùng lúc xếp hàng ở khoá theo user
/// trong repository.
/// </summary>
public class RecurringTrackingJob
{
    private readonly IRecurringDetectionService _detection;
    private readonly ICacheService _cache;

    public RecurringTrackingJob(IRecurringDetectionService detection, ICacheService cache)
    {
        _detection = detection;
        _cache = cache;
    }

    public async Task RunAsync(Guid userId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(CacheKeys.RecurringTrackingQueued(userId), ct);
        await _detection.TrackForUserAsync(userId, ct);
    }
}

/// <summary>
/// Xếp hàng <see cref="RecurringTrackingJob"/> khi user có khoản định kỳ đang theo dõi cùng chiều với giao dịch vừa xác nhận. Một dấu trong cache
/// (có hạn) chặn xếp nhiều lượt cho cùng user khi giao dịch về dồn dập. Mọi lỗi chỉ ghi loại lỗi: không bao giờ làm hỏng giao dịch đã lưu.
/// </summary>
public class HangfireRecurringTrackingTrigger : IRecurringTrackingTrigger
{
    private static readonly TimeSpan QueuedMarkerTtl = TimeSpan.FromMinutes(10);

    private readonly IBackgroundJobClient _jobs;
    private readonly IRecurringItemRepository _items;
    private readonly ICacheService _cache;
    private readonly ILogger<HangfireRecurringTrackingTrigger> _logger;

    public HangfireRecurringTrackingTrigger(
        IBackgroundJobClient jobs, IRecurringItemRepository items, ICacheService cache, ILogger<HangfireRecurringTrackingTrigger> logger)
    {
        _jobs = jobs;
        _items = items;
        _cache = cache;
        _logger = logger;
    }

    public async Task TransactionConfirmedAsync(Guid userId, TransactionType type, CancellationToken ct = default)
    {
        try
        {
            if (DirectionOf(type) is not { } direction
                || !(await _items.ListAsync(userId, RecurringStatus.Active, ct)).Any(i => i.Direction == direction && i.CounterpartyKey is not null))
            {
                return;
            }

            var marker = CacheKeys.RecurringTrackingQueued(userId);
            if (await _cache.GetAsync<bool?>(marker, ct) is true)
            {
                return;
            }

            await _cache.SetAsync<bool?>(marker, true, QueuedMarkerTtl, ct);
            _jobs.Enqueue<RecurringTrackingJob>(job => job.RunAsync(userId, CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not enqueue recurring tracking: {ExceptionType}", ex.GetType().Name);
        }
    }

    private static RecurringDirection? DirectionOf(TransactionType type) => type switch
    {
        TransactionType.Debit => RecurringDirection.Outflow,
        TransactionType.Credit => RecurringDirection.Inflow,
        _ => null,
    };
}
