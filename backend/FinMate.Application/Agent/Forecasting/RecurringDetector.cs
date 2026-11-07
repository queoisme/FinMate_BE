using FinMate.Application.Common;
using FinMate.Application.Reports;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Agent;

/// <summary>Một khoản có vẻ định kỳ phát hiện từ lịch sử. CHỈ là đề xuất: người dùng xác nhận thì mới lưu thành <see cref="RecurringItem"/>.</summary>
public sealed record RecurringCandidate(string Name, long AmountVnd, RecurringKind Kind, int DayOfMonth, int Months);

/// <summary>
/// Phát hiện khoản định kỳ: cùng tên (người nhận / mô tả), cùng chiều thu hoặc chi, xuất hiện ở ít nhất <see cref="AgentLimits.RecurringMinMonths"/> tháng khác nhau với số tiền xấp xỉ nhau và
/// cùng một khoảng ngày trong tháng. Khoản chỉ gặp một lần (hoặc thất thường) bị bỏ qua. Chuyển tiền giữa các ví của chính người dùng KHÔNG bao giờ là khoản định kỳ: nó đổi chỗ giữ tiền,
/// không phải tiền ra hay vào. Chỉ giao dịch đã xác nhận, chưa xóa.
/// </summary>
public static class RecurringDetector
{
    public static string NormalizeName(string? name) => string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static IReadOnlyList<RecurringCandidate> Detect(IReadOnlyList<Transaction> transactions)
    {
        var usable = transactions
            .Where(t => t.Status == TransactionStatus.Confirmed && t.DeletedAt is null && t.TransactionType is TransactionType.Debit or TransactionType.Credit)
            .Select(t => (Transaction: t, Name: (t.MerchantName ?? t.Description)?.Trim() ?? string.Empty))
            .Where(x => x.Name.Length > 0);

        var candidates = new List<RecurringCandidate>();
        foreach (var group in usable.GroupBy(x => (Key: NormalizeName(x.Name), x.Transaction.TransactionType)))
        {
            // mỗi tháng tính một lần (giao dịch sớm nhất): hai lần trả trong cùng tháng không biến một khoản thất thường thành "định kỳ"
            var perMonth = group
                .GroupBy(x => VietnamTime.YearMonthOf(x.Transaction.TransactedAt))
                .Select(g => g.OrderBy(x => x.Transaction.TransactedAt).First().Transaction)
                .ToList();
            if (perMonth.Count < AgentLimits.RecurringMinMonths)
            {
                continue;
            }

            var amounts = perMonth.Select(t => t.AmountCents).OrderBy(v => v).ToArray();
            var days = perMonth.Select(t => VietnamTime.ToLocal(t.TransactedAt).Day).OrderBy(v => v).ToArray();
            var medianAmount = Statistics.Quantile(amounts, 0.5);
            var medianDay = (int)Statistics.Quantile(days.Select(d => (long)d).ToArray(), 0.5);

            var steady = amounts.All(a => Math.Abs(a - medianAmount) * 100 <= medianAmount * AgentLimits.RecurringAmountTolerancePercent)
                         && days.All(d => Math.Abs(d - medianDay) <= AgentLimits.RecurringDayTolerance);
            if (!steady || medianAmount <= 0)
            {
                continue;
            }

            var name = group.GroupBy(x => x.Name).OrderByDescending(g => g.Count()).First().Key;
            candidates.Add(new RecurringCandidate(name, medianAmount, group.Key.TransactionType == TransactionType.Credit ? RecurringKind.Inflow : RecurringKind.Obligation, medianDay, perMonth.Count));
        }

        return candidates.OrderBy(c => c.Kind).ThenByDescending(c => c.AmountVnd).ToList();
    }
}
