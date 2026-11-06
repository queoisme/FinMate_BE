using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;

namespace FinMate.Application.Subscriptions;

/// <summary>
/// Đơn đã có tiền mà chưa cấp được gói phải có người biết. Mỗi đơn mở một sự cố, một lần: người xử lý đóng sự cố theo quy trình có sẵn và sự cố không mở lại.
/// Tiêu đề và chi tiết không có số tiền hay người dùng.
/// </summary>
public sealed class SubscriptionReviewMonitorSource : IMonitorSource
{
    private const int Batch = 100;

    private readonly ISubscriptionRepository _repository;
    private readonly IIncidentService _incidents;

    public SubscriptionReviewMonitorSource(ISubscriptionRepository repository, IIncidentService incidents)
    {
        _repository = repository;
        _incidents = incidents;
    }

    public async Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        foreach (var order in await _repository.ListUnalertedReviewOrdersAsync(Batch, ct))
        {
            await _incidents.RaiseAsync(
                new IncidentSignal(
                    $"subscription_review:{order.Id}",
                    IncidentSource.Payment,
                    MonitoredServices.Payments.Key,
                    MonitoredServices.Payments.Severity,
                    "Có đơn đăng ký đã trả tiền nhưng chưa cấp được gói",
                    $"order={order.Id} reason={order.ReviewReason}"),
                1,
                now,
                ct);
            await _repository.MarkReviewAlertedAsync(order.Id, now, ct);
        }
    }
}
