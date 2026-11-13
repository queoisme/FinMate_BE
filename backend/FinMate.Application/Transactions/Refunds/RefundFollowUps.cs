using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Refunds;

/// <summary>
/// Việc phụ khi một khoản chi được hoàn ĐỦ hoặc bị ĐẢO (Phase 81). Chỉ đổi dữ liệu đang được theo dõi, lưu cùng thao tác hoàn/đảo.
/// <list type="bullet">
/// <item>R2: đánh dấu bất thường còn mở của khoản chi tự đóng với lý do <c>Refunded</c>/<c>Reversed</c>, không có thông báo nào.</item>
/// <item>R1: khoản chi bị đảo mà chính nó là lần trả đã đẩy ngày đến hạn của một khoản định kỳ → khoản định kỳ trở về CHƯA TRẢ (lùi một chu kỳ),
/// để nhắc hạn và quá hạn chạy lại đúng. Hoàn tiền (kể cả hoàn đủ) không đổi trạng thái đã trả: người dùng đã trả hoá đơn đó thật.</item>
/// </list>
/// </summary>
public interface IRefundFollowUps
{
    Task OnRefundedAsync(Guid userId, Transaction original, long refundedCents, CancellationToken ct = default);
}

public sealed class RefundFollowUps : IRefundFollowUps
{
    // Đủ xa để thấy lần trả trước của khoản theo năm.
    private const int PreviousPaymentLookbackDays = 400;

    private readonly IAnomalyRepository _anomalies;
    private readonly IRecurringItemRepository _recurring;
    private readonly IForecastDataRepository _data;

    public RefundFollowUps(IAnomalyRepository anomalies, IRecurringItemRepository recurring, IForecastDataRepository data)
    {
        _anomalies = anomalies;
        _recurring = recurring;
        _data = data;
    }

    public async Task OnRefundedAsync(Guid userId, Transaction original, long refundedCents, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (original.IsReversed)
        {
            await _anomalies.ResolveOpenForTransactionAsync(userId, original.Id, AnomalyResolution.Reversed, now, ct);
            await RewindRecurringAsync(userId, original, now, ct);
        }
        else if (refundedCents >= original.AmountCents)
        {
            await _anomalies.ResolveOpenForTransactionAsync(userId, original.Id, AnomalyResolution.Refunded, now, ct);
        }
    }

    private async Task RewindRecurringAsync(Guid userId, Transaction original, DateTimeOffset now, CancellationToken ct)
    {
        var key = CounterpartyKey.From(original.MerchantName);
        if (key is null)
        {
            return;
        }

        var advancedByThis = (await _recurring.ListAllTrackedAsync(userId, ct))
            .Where(r => r.Direction == RecurringDirection.Outflow
                && r.DeletedAt is null
                && r.CounterpartyKey == key
                && r.LastSeenAt == original.TransactedAt)
            .ToList();
        if (advancedByThis.Count == 0)
        {
            return;
        }

        // Lần trả hợp lệ liền trước (không kể chính khoản bị đảo, mà DB vẫn chưa thấy là đã đảo vì chưa lưu).
        var date = VietnamTime.DateOf(original.TransactedAt);
        var history = await _data.GetTransactionsAsync(userId, date.AddDays(-PreviousPaymentLookbackDays), date, ct);
        var previous = history
            .Where(t => t.Id != original.Id && t.Type == TransactionType.Debit && t.CounterpartyKey == key && t.TransactedAt < original.TransactedAt)
            .MaxBy(t => t.TransactedAt);

        foreach (var item in advancedByThis)
        {
            item.NextDueDate = RecurringSchedule.Previous(item.NextDueDate, item.Cadence, RecurringItemMapping.PreferredDay(item.Cadence, item.NextDueDate));
            item.Occurrences = Math.Max(0, item.Occurrences - 1);
            item.LastSeenAt = previous?.TransactedAt;
            item.LastSeenAmountCents = previous?.AmountCents;
            item.UpdatedAt = now;
        }
    }
}
