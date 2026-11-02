using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Cập nhật một khoản định kỳ theo các giao dịch mới thấy: mỗi lần đến hạn đã có thanh toán khớp thì tăng số lần, ghi lần thấy gần nhất
/// và đẩy ngày đến hạn sang lần kế tiếp. Khoản đang đề xuất mà đủ số lần lặp thì tự ghi nhận (sơ đồ 5a: "Đủ số lần lặp để chắc chắn?").
/// </summary>
public static class RecurringTracker
{
    /// <summary>Trần số vòng đuổi kịp trong một lần chạy, chặn vòng lặp vô hạn nếu dữ liệu bất thường.</summary>
    private const int MaxCatchUpSteps = 60;

    /// <returns>true nếu khoản bị thay đổi.</returns>
    public static bool Advance(RecurringItem item, IReadOnlyList<ForecastTransaction> history, ForecastRules rules, DateTimeOffset now)
    {
        if (item.Status is not (RecurringStatus.Active or RecurringStatus.Suggested or RecurringStatus.Paused) || item.CounterpartyKey is null)
        {
            return false;
        }

        var changed = false;
        for (var step = 0; step < MaxCatchUpSteps; step++)
        {
            var payment = RecurringMatcher.FindPayment(item.ToView(), item.NextDueDate, history, rules);
            if (payment is null || (item.LastSeenAt is { } seen && payment.TransactedAt <= seen))
            {
                break;
            }

            item.Occurrences++;
            item.LastSeenAt = payment.TransactedAt;
            item.LastSeenAmountCents = payment.AmountCents;
            if (item.Source == RecurringSource.Detected)
            {
                item.AmountCents = payment.AmountCents;
            }

            item.NextDueDate = RecurringSchedule.Next(item.NextDueDate, item.Cadence, RecurringItemMapping.PreferredDay(item.Cadence, item.NextDueDate));
            changed = true;
        }

        if (item.Status == RecurringStatus.Suggested && item.Occurrences >= rules.AutoActivateOccurrences)
        {
            item.Status = RecurringStatus.Active;
            changed = true;
        }

        if (changed)
        {
            item.UpdatedAt = now;
        }

        return changed;
    }
}
