using FinMate.Application.Subscriptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record SubscriptionOrderPage(IReadOnlyList<SubscriptionOrder> Items, string? NextCursor);

public interface ISubscriptionRepository
{
    Task<List<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct = default);
    Task<SubscriptionPlan?> GetPlanForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<SubscriptionPlan?> GetPlanAsync(Guid id, CancellationToken ct = default);
    Task<SubscriptionPlan?> GetDefaultFreePlanAsync(CancellationToken ct = default);
    Task<bool> PlanCodeExistsAsync(string code, CancellationToken ct = default);
    void AddPlan(SubscriptionPlan plan);
    void RemoveFeatures(IEnumerable<SubscriptionPlanFeature> features);
    Task SaveChangesAsync(CancellationToken ct = default);

    void AddOrder(SubscriptionOrder order);
    Task<SubscriptionOrder?> GetOrderByInvoiceAsync(string invoiceNumber, CancellationToken ct = default);
    Task<SubscriptionOrder?> GetUserOrderByInvoiceAsync(Guid userId, string invoiceNumber, CancellationToken ct = default);
    Task<SubscriptionOrderPage> ListUserOrdersAsync(Guid userId, string? cursor, int limit, CancellationToken ct = default);
    Task<(List<SubscriptionOrder> Items, int Total)> ListOrdersAsync(SubscriptionOrderStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<int> CountPendingOrdersAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Đơn đến lượt hỏi lại SePay: đang chờ, và đơn đã hủy hoặc hết hạn còn trong <paramref name="lateSince"/>, vì tiền vẫn có thể về từ mã QR đã mở. Đơn đợi lâu nhất xếp trước.</summary>
    Task<List<SubscriptionOrder>> ListOrdersDueForCheckAsync(DateTimeOffset now, DateTimeOffset lateSince, int limit, CancellationToken ct = default);
    Task SetNextCheckAsync(Guid orderId, DateTimeOffset nextCheckAt, CancellationToken ct = default);

    /// <summary>Chỉ đổi khi đơn vẫn đang <c>Pending</c>.</summary>
    Task MarkPendingAsync(Guid orderId, SubscriptionOrderStatus status, CancellationToken ct = default);

    /// <summary>Đơn chưa ghi nhận tiền chuyển sang chờ người xử lý. False nếu đơn đã được xử lý từ trước.</summary>
    Task<bool> MarkNeedsReviewAsync(Guid orderId, string reason, CancellationToken ct = default);

    /// <summary>Trong một DB transaction, khóa đơn và người dùng, đánh dấu đã trả và thêm kỳ nối tiếp kỳ cuối của người đó.</summary>
    Task<PaymentOutcome> RecordPaymentAsync(Guid orderId, string? sepayTransactionId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Trong một DB transaction, hủy đơn đã trả và kỳ của nó, dời các kỳ sau lên. False nếu đơn không ở trạng thái đã trả.</summary>
    Task<bool> VoidPaymentAsync(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    Task<List<UserSubscriptionPeriod>> ListCurrentAndUpcomingPeriodsAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Kỳ sắp hết hạn của những người chưa có kỳ nối tiếp và chưa nhận đủ nhắc nhở.</summary>
    Task<List<UserSubscriptionPeriod>> ListExpiringWithoutRenewalAsync(DateTimeOffset now, DateTimeOffset firstReminderUntil, DateTimeOffset lastReminderUntil, int limit, CancellationToken ct = default);

    Task<List<SubscriptionOrder>> ListUnalertedReviewOrdersAsync(int limit, CancellationToken ct = default);
    Task MarkReviewAlertedAsync(Guid orderId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Giữ một lượt nếu chưa đạt hạn mức. Một câu lệnh duy nhất, nên request song song không vượt được.</summary>
    Task<bool> TryConsumeAsync(Guid userId, string featureKey, DateOnly month, int limit, CancellationToken ct = default);
    Task ReleaseAsync(Guid userId, string featureKey, DateOnly month, CancellationToken ct = default);
    Task<List<SubscriptionUsage>> ListUsageAsync(Guid userId, DateOnly month, CancellationToken ct = default);
}

/// <summary>Tra đơn trên cổng thanh toán. Không tra được thì ném <c>SePayUnavailableException</c>; đơn không tồn tại trả null.</summary>
public interface ISePayClient
{
    Task<SePayOrderInfo?> GetOrderAsync(string invoiceNumber, CancellationToken ct = default);
}
