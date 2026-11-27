using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Một lần trả với số tiền lệch quá dung sai so với khoản định kỳ (sơ đồ 5a: "Tạo thông báo thay đổi giá").</summary>
public sealed record PriceChange(Guid RecurringItemId, string Name, long OldAmountCents, long NewAmountCents, DateOnly PaidOn);

/// <summary>Phase 92 (BR-INS-03): tracker đã nhận giao dịch này là lần trả cho kỳ hạn <paramref name="DueDate"/>; người gọi lưu thành liên kết.</summary>
public sealed record RecurringMatch(Guid RecurringItemId, DateOnly DueDate, Guid TransactionId);

/// <summary>
/// Cập nhật một khoản định kỳ theo các giao dịch mới thấy: mỗi lần đến hạn đã có thanh toán khớp thì tăng số lần, ghi lần thấy gần nhất
/// và đẩy ngày đến hạn sang lần kế tiếp. Khoản đang đề xuất mà đủ số lần lặp thì tự ghi nhận (sơ đồ 5a: "Đủ số lần lặp để chắc chắn?").
/// </summary>
public static class RecurringTracker
{
    private const int MinFixedPricePayments = 2;
    private const int MinFixedPriceRisePercent = 1;

    /// <summary>
    /// Ngưỡng dung sai dành cho hóa đơn dao động như tiền điện. Tiền nhà, internet trả đúng một số mỗi tháng, nên tăng trong ngưỡng cũng là tăng giá thật.
    /// Cần 2 lần trả giống hệt nhau cách nhau đúng một kỳ trước đó, tăng từ 1% trở lên, và chỉ xét khoản hằng tháng: giao dịch thường và mua lặp lại không phải giá cố định.
    /// Khoản hằng năm không có đủ 2 lần trong cửa sổ lịch sử nên không xét được.
    /// </summary>
    private static bool FixedPriceRose(RecurringItem item, ForecastTransaction payment, IReadOnlyList<ForecastTransaction> history, ForecastRules rules)
    {
        if (item.Cadence != RecurringCadence.Monthly || (payment.AmountCents - item.AmountCents) * 100 < item.AmountCents * MinFixedPriceRisePercent)
        {
            return false;
        }

        var earlier = history
            .Where(t => t.CounterpartyKey == item.CounterpartyKey && t.Type == payment.Type && t.TransactedAt < payment.TransactedAt)
            .OrderByDescending(t => t.TransactedAt)
            .Take(MinFixedPricePayments)
            .ToList();
        return earlier.Count == MinFixedPricePayments
            && earlier.All(t => t.AmountCents == item.AmountCents)
            && earlier[0].Date.DayNumber - earlier[1].Date.DayNumber >= rules.MonthlyMinIntervalDays;
    }

    /// <summary>Trần số vòng đuổi kịp trong một lần chạy, chặn vòng lặp vô hạn nếu dữ liệu bất thường.</summary>
    private const int MaxCatchUpSteps = 60;

    /// <returns>true nếu khoản bị thay đổi.</returns>
    public static bool Advance(RecurringItem item, IReadOnlyList<ForecastTransaction> history, ForecastRules rules, DateTimeOffset now)
        => Advance(item, history, rules, now, []);

    /// <param name="priceChanges">Nhận các lần đổi giá phát hiện được.</param>
    /// <param name="matches">Phase 92: nhận cặp (kỳ hạn, giao dịch) đã khớp, để lưu thành liên kết thanh toán.</param>
    /// <param name="linkedTransactionIds">Phase 92: giao dịch đã gắn một khoản định kỳ (còn sống) — không khớp lại.</param>
    public static bool Advance(
        RecurringItem item, IReadOnlyList<ForecastTransaction> history, ForecastRules rules, DateTimeOffset now, ICollection<PriceChange> priceChanges,
        ICollection<RecurringMatch>? matches = null, IReadOnlySet<Guid>? linkedTransactionIds = null)
    {
        if (item.Status is not (RecurringStatus.Active or RecurringStatus.Suggested or RecurringStatus.Paused) || item.CounterpartyKey is null)
        {
            return false;
        }

        var candidates = linkedTransactionIds is null ? history : history.Where(t => !linkedTransactionIds.Contains(t.Id)).ToList();
        var changed = false;
        for (var step = 0; step < MaxCatchUpSteps && !item.ToView().Exhausted; step++)
        {
            // Mỗi vòng xét đúng một kỳ hạn rồi đẩy sang kỳ sau, nên tối đa một giao dịch được nhận cho mỗi kỳ (BR-INS-03).
            var payment = RecurringMatcher.FindPayment(item.ToView(), item.NextDueDate, candidates, rules);
            if (payment is null || (item.LastSeenAt is { } seen && payment.TransactedAt <= seen))
            {
                break;
            }

            matches?.Add(new RecurringMatch(item.Id, item.NextDueDate, payment.Id));
            item.LivePaymentCount++;
            item.Occurrences++;
            item.LastSeenAt = payment.TransactedAt;
            item.LastSeenAmountCents = payment.AmountCents;
            if (!rules.AmountWithinTolerance(item.AmountCents, payment.AmountCents))
            {
                // Giá đổi thật: theo giá mới cho cả khoản user tự thêm, nếu không dự báo sẽ trừ sai số tiền mãi.
                priceChanges.Add(new PriceChange(item.Id, item.Name, item.AmountCents, payment.AmountCents, payment.Date));
                item.AmountCents = payment.AmountCents;
            }
            else
            {
                if (FixedPriceRose(item, payment, history, rules))
                {
                    priceChanges.Add(new PriceChange(item.Id, item.Name, item.AmountCents, payment.AmountCents, payment.Date));
                }

                // Khoản user tự thêm giữ nguyên số họ nhập: một lần trả thừa không được làm dự báo lệch mãi.
                if (item.Source == RecurringSource.Detected)
                {
                    item.AmountCents = payment.AmountCents;
                }
            }

            item.NextDueDate = RecurringSchedule.Next(item.NextDueDate, item.Cadence, RecurringSchedule.DayOf(item));
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
