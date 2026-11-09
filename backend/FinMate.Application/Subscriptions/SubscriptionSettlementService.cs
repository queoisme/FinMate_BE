using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Subscriptions;

public enum SettlementResult
{
    Granted,
    AlreadySettled,
    NotPaid,
    UnknownOrder,
    NeedsReview,
}

public interface ISubscriptionSettlementService
{
    /// <summary>So header IPN với key đã cấu hình. Chưa cấu hình key thì từ chối tất cả.</summary>
    Task<bool> IsValidIpnKeyAsync(string? providedKey, CancellationToken ct = default);

    /// <summary>Đường duy nhất cấp quyền lợi, dùng chung cho IPN và job đối soát: tự hỏi SePay rồi so với đơn, không tin nội dung IPN.</summary>
    Task<SettlementResult> SettleOrderAsync(string invoiceNumber, CancellationToken ct = default);

    /// <summary>
    /// Áp dụng thông báo hủy giao dịch. Đơn đã trả: chỉ hủy khi đúng giao dịch đã ghi nhận, vì đơn có thể có lần thử thất bại trước đó.
    /// Đơn chưa ghi nhận tiền: chuyển cho người kiểm tra, để job đối soát không cấp gói cho khoản đã bị hủy.
    /// </summary>
    Task<bool> VoidOrderAsync(string invoiceNumber, string? transactionId, CancellationToken ct = default);

    /// <summary>Xử lý các đơn IPN không về, và cho đơn quá hạn hết hiệu lực.</summary>
    Task<int> ReconcileAsync(DateTimeOffset now, CancellationToken ct = default);

    Task<int> SendExpiryRemindersAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class SubscriptionSettlementService : ISubscriptionSettlementService
{
    public const string ReasonAmountMismatch = "amount_mismatch";
    public const string ReasonUserMissing = "user_missing";
    public const string ReasonVoidBeforePayment = "void_before_payment";

    private const string ActionActivated = "subscription_activated";
    private const string ActionExpiring = "subscription_expiring";
    private const int ReconcileBatch = 200;
    private const int ReminderBatch = 500;

    /// <summary>Lỗi liên tiếp từng này lần thì coi như SePay đang hỏng và dừng lượt này, để không ngồi chờ timeout của cả lô.</summary>
    private const int MaxConsecutiveFailures = 3;
    private static readonly TimeSpan IpnGrace = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LatePaymentWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan FirstReminder = TimeSpan.FromDays(7);
    private static readonly TimeSpan LastReminder = TimeSpan.FromDays(1);

    private readonly ISubscriptionRepository _repository;
    private readonly ISePayClient _sepay;
    private readonly ISecretVault _vault;
    private readonly IPushNotificationService _push;
    private readonly IAuditLogService _audit;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<SubscriptionSettlementService> _logger;

    public SubscriptionSettlementService(
        ISubscriptionRepository repository,
        ISePayClient sepay,
        ISecretVault vault,
        IPushNotificationService push,
        IAuditLogService audit,
        ISystemConfigProvider config,
        ILogger<SubscriptionSettlementService> logger)
    {
        _repository = repository;
        _sepay = sepay;
        _vault = vault;
        _push = push;
        _audit = audit;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> IsValidIpnKeyAsync(string? providedKey, CancellationToken ct = default)
    {
        var expected = await _vault.GetAsync(BackendSecrets.SepayIpnSecretKey, ct);
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        // So hai bản băm để độ dài key không lộ qua thời gian phản hồi.
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(providedKey)));
    }

    public async Task<SettlementResult> SettleOrderAsync(string invoiceNumber, CancellationToken ct = default)
    {
        var order = await _repository.GetOrderByInvoiceAsync(invoiceNumber, ct);
        if (order is null)
        {
            return SettlementResult.UnknownOrder;
        }

        if (IsFinal(order.Status))
        {
            return SettlementResult.AlreadySettled;
        }

        var remote = await _sepay.GetOrderAsync(invoiceNumber, ct);
        if (remote?.Status == SePayOrderInfo.StatusCancelled)
        {
            await _repository.MarkPendingAsync(order.Id, SubscriptionOrderStatus.Cancelled, ct);
            return SettlementResult.NotPaid;
        }

        if (remote?.Status != SePayOrderInfo.StatusCaptured)
        {
            return SettlementResult.NotPaid;
        }

        if (remote.Amount != order.AmountCents || remote.Currency != SePayOrderInfo.CurrencyVnd)
        {
            await FlagForReviewAsync(order, ReasonAmountMismatch, ct);
            return SettlementResult.NeedsReview;
        }

        return await RecordPaymentAsync(order, remote.TransactionId, ct);
    }

    public async Task<bool> VoidOrderAsync(string invoiceNumber, string? transactionId, CancellationToken ct = default)
    {
        var order = await _repository.GetOrderByInvoiceAsync(invoiceNumber, ct);
        if (order is null || order.Status is SubscriptionOrderStatus.Voided or SubscriptionOrderStatus.NeedsReview)
        {
            return false;
        }

        if (order.Status != SubscriptionOrderStatus.Paid)
        {
            await FlagForReviewAsync(order, ReasonVoidBeforePayment, ct);
            return false;
        }

        if (transactionId is not null && order.SepayTransactionId is not null && transactionId != order.SepayTransactionId)
        {
            _logger.LogWarning("Bỏ qua thông báo hủy của đơn {OrderId}: không phải giao dịch đã ghi nhận", order.Id);
            return false;
        }

        if (!await _repository.VoidPaymentAsync(order.Id, DateTimeOffset.UtcNow, ct))
        {
            return false;
        }

        await _audit.LogAsync(AuditEvents.SubscriptionOrderVoided, order.UserId, null, new { orderId = order.Id, order.PlanCode }, ct);
        return true;
    }

    public async Task<int> ReconcileAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settled = 0;
        var failures = 0;
        foreach (var order in await _repository.ListOrdersDueForCheckAsync(now, now.Subtract(LatePaymentWindow), ReconcileBatch, ct))
        {
            try
            {
                var result = await SettleOrderAsync(order.InvoiceNumber, ct);
                failures = 0;
                settled += result == SettlementResult.Granted ? 1 : 0;
                await _repository.SetNextCheckAsync(order.Id, now.Add(SubscriptionPeriods.CheckIntervalFor(now - order.CreatedAt)), ct);

                // Chỉ tới đây khi SePay đã trả lời được, nên đơn không bị coi là hết hạn trong lúc SePay đang hỏng.
                // MarkPendingAsync không đụng tới đơn đã hủy hoặc đã hết hạn từ trước.
                if (result == SettlementResult.NotPaid && order.ExpiresAt <= now)
                {
                    await _repository.MarkPendingAsync(order.Id, SubscriptionOrderStatus.Expired, ct);
                }
            }
            catch (SePayUnavailableException)
            {
                // Dời đơn này ra sau, để một đơn SePay luôn báo lỗi không chặn các đơn khác. Lỗi được IProviderFailureTracker đếm và mở sự cố.
                await _repository.SetNextCheckAsync(order.Id, now.Add(IpnGrace), ct);
                if (++failures >= MaxConsecutiveFailures)
                {
                    _logger.LogWarning("Đối soát đơn đăng ký dừng giữa chừng vì không tra được SePay");
                    break;
                }
            }
        }

        return settled;
    }

    public async Task<int> SendExpiryRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // Cờ tắt thì không mua được để gia hạn, nên nhắc chỉ gây bực.
        if (!(await _config.GetAsync(ct)).Document.Features.Subscriptions)
        {
            return 0;
        }

        var sent = 0;
        foreach (var period in await _repository.ListExpiringWithoutRenewalAsync(now, now.Add(FirstReminder), now.Add(LastReminder), ReminderBatch, ct))
        {
            var isLast = period.EndsAt - now <= LastReminder;
            period.Reminded7dAt ??= now;
            if (isLast)
            {
                period.Reminded1dAt = now;
            }

            await _repository.SaveChangesAsync(ct);
            await _push.NotifyAsync(
                period.UserId,
                "Gói đăng ký sắp hết hạn",
                $"Gói {period.PlanName} của bạn sắp hết hạn. Gia hạn để tiếp tục dùng các chức năng của gói.",
                new Dictionary<string, string> { ["action"] = ActionExpiring },
                ct);
            sent++;
        }

        return sent;
    }

    private async Task<SettlementResult> RecordPaymentAsync(SubscriptionOrder order, string? transactionId, CancellationToken ct)
    {
        switch (await _repository.RecordPaymentAsync(order.Id, transactionId, DateTimeOffset.UtcNow, ct))
        {
            case PaymentOutcome.Granted:
                await _audit.LogAsync(AuditEvents.SubscriptionOrderPaid, order.UserId, null, new { orderId = order.Id, order.PlanCode }, ct);
                await _push.NotifyAsync(
                    order.UserId!.Value,
                    "Thanh toán thành công",
                    $"Gói {order.PlanName} đã được ghi nhận cho tài khoản của bạn.",
                    new Dictionary<string, string> { ["action"] = ActionActivated },
                    ct);
                return SettlementResult.Granted;
            case PaymentOutcome.UserMissing:
                await _audit.LogAsync(AuditEvents.SubscriptionOrderNeedsReview, null, null, new { orderId = order.Id, reason = ReasonUserMissing }, ct);
                return SettlementResult.NeedsReview;
            default:
                return SettlementResult.AlreadySettled;
        }
    }

    private async Task FlagForReviewAsync(SubscriptionOrder order, string reason, CancellationToken ct)
    {
        if (await _repository.MarkNeedsReviewAsync(order.Id, reason, ct))
        {
            await _audit.LogAsync(AuditEvents.SubscriptionOrderNeedsReview, order.UserId, null, new { orderId = order.Id, reason }, ct);
            _logger.LogWarning("Đơn đăng ký {OrderId} cần người kiểm tra: {Reason}", order.Id, reason);
        }
    }

    /// <summary><c>Cancelled</c> và <c>Expired</c> không phải cuối: khách có thể vẫn trả tiền từ mã QR đã mở, và tiền đã về thì phải được ghi nhận.</summary>
    private static bool IsFinal(SubscriptionOrderStatus status)
        => status is SubscriptionOrderStatus.Paid or SubscriptionOrderStatus.Voided or SubscriptionOrderStatus.NeedsReview;
}
