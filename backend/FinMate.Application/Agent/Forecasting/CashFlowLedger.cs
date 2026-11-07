namespace FinMate.Application.Agent;

/// <summary>Một khoản đã lên lịch vào/ra ở một ngày. <see cref="Settled"/> = đã có giao dịch thanh toán thật nên số dư mở đầu đã phản ánh nó: không được trừ/cộng lần nữa.</summary>
public sealed record ScheduledItem(string Id, string Name, long AmountVnd, DateOnly Date, EvidenceSource Source, bool Settled = false);

public sealed record HypotheticalPurchase(long AmountVnd, DateOnly Date);

public sealed record DayBalance(DateOnly Date, long BalanceVnd);

/// <summary>Chuỗi số dư dự kiến từng ngày. Số âm được giữ nguyên ("thiếu dự kiến"): không bao giờ ép về 0 rồi che nguy cơ.</summary>
public sealed record CashFlowProjection(IReadOnlyList<DayBalance> Days)
{
    public long MinimumBalanceVnd => Days.Min(d => d.BalanceVnd);

    /// <summary>Ngày ĐẦU TIÊN số dư chạm mức thấp nhất.</summary>
    public DateOnly MinimumDate => Days.First(d => d.BalanceVnd == MinimumBalanceVnd).Date;
}

/// <summary>
/// Sổ dòng tiền theo ngày (Flow 6 §6.2): <c>số dư(d) = số dư mở đầu + thu đã lên lịch đến d − khoản phải trả đến d − khoản dự trữ sinh hoạt − khoản mua giả định (nếu ngày mua ≤ d)</c>.
/// Khoản dự trữ là mức tiền phải giữ lại trong cả kỳ tính. Thuần hàm trên số nguyên đồng; khoản có <see cref="ScheduledItem.Settled"/> bị bỏ; khoản ngoài [from, to] không ảnh hưởng
/// (trước <c>from</c> đã nằm trong số dư mở đầu, sau <c>to</c> thuộc kỳ khác).
/// </summary>
public static class CashFlowLedger
{
    public static CashFlowProjection Project(
        long openingBalanceVnd,
        IEnumerable<ScheduledItem> inflows,
        IEnumerable<ScheduledItem> obligations,
        long reserveVnd,
        HypotheticalPurchase? purchase,
        DateOnly from,
        DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("Kỳ tính phải có ngày kết thúc không trước ngày bắt đầu.", nameof(to));
        }

        var live = (IEnumerable<ScheduledItem> items) => items.Where(i => !i.Settled && i.Date >= from && i.Date <= to).ToList();
        var ins = live(inflows);
        var outs = live(obligations);

        var days = new List<DayBalance>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var balance = openingBalanceVnd
                          + ins.Where(i => i.Date <= day).Sum(i => i.AmountVnd)
                          - outs.Where(i => i.Date <= day).Sum(i => i.AmountVnd)
                          - reserveVnd
                          - (purchase is not null && purchase.Date <= day ? purchase.AmountVnd : 0);
            days.Add(new DayBalance(day, balance));
        }

        return new CashFlowProjection(days);
    }
}
