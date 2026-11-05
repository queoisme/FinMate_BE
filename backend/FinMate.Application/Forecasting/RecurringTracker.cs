using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Một lần trả với số tiền lệch quá dung sai so với khoản định kỳ (sơ đồ 5a: "Tạo thông báo thay đổi giá").</summary>
public sealed record PriceChange(Guid RecurringItemId, string Name, long OldAmountCents, long NewAmountCents, DateOnly PaidOn);

/// <summary>
/// Cập nhật một khoản định kỳ theo các giao dịch mới thấy: mỗi lần đến hạn đã có thanh toán khớp thì tăng số lần, ghi lần thấy gần nhất
/// và đẩy ngày đến hạn sang lần kế tiếp. Khoản đang đề xuất mà đủ số lần lặp thì tự ghi nhận (sơ đồ 5a: "Đủ số lần lặp để chắc chắn?").
/// </summary>
public static class RecurringTracker
{
    private const int MinFixedPricePayments = 2;

    /// <summary>
    /// Ngưỡng dung sai dành cho hóa đơn dao động như tiền điện. Tiền nhà, internet, gói đăng ký trả đúng một số mỗi kỳ, nên tăng bao nhiêu cũng là tăng giá thật.
    /// Cần 2 lần trả giống hệt nhau trước đó mới coi là giá cố định. Chỉ xét khi tăng.
    /// </summary>
    private static bool FixedPriceRose(RecurringItem item, ForecastTransaction payment, IReadOnlyList<ForecastTransaction> history)
    {
        if (payment.AmountCents <= item.AmountCents)
        {
            return false;
        }

        var earlier = history
            .Where(t => t.CounterpartyKey == item.CounterpartyKey && t.Type == payment.Type && t.TransactedAt < payment.TransactedAt)
            .OrderByDescending(t => t.TransactedAt)
            .Take(MinFixedPricePayments)
            .ToList();
        return earlier.Count == MinFixedPricePayments && earlier.All(t => t.AmountCents == item.AmountCents);
    }

    /// <summary>Trần số vòng đuổi kịp trong một lần chạy, chặn vòng lặp vô hạn nếu dữ liệu bất thường.</summary>
    private const int MaxCatchUpSteps = 60;

    /// <returns>true nếu khoản bị thay đổi.</returns>
    public static bool Advance(RecurringItem item, IReadOnlyList<ForecastTransaction> history, ForecastRules rules, DateTimeOffset now)
        => Advance(item, history, rules, now, []);

    /// <param name="priceChanges">Nhận các lần đổi giá phát hiện được.</param>
    public static bool Advance(
        RecurringItem item, IReadOnlyList<ForecastTransaction> history, ForecastRules rules, DateTimeOffset now, ICollection<PriceChange> priceChanges)
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
            if (!rules.AmountWithinTolerance(item.AmountCents, payment.AmountCents) || FixedPriceRose(item, payment, history))
            {
                // Giá đổi thật: theo giá mới cho cả khoản user tự thêm, nếu không dự báo sẽ trừ sai số tiền mãi.
                priceChanges.Add(new PriceChange(item.Id, item.Name, item.AmountCents, payment.AmountCents, payment.Date));
                item.AmountCents = payment.AmountCents;
            }
            else if (item.Source == RecurringSource.Detected)
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
