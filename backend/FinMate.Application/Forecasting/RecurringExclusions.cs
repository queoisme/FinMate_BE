using FinMate.Application.Common;
using FinMate.Domain.Entities;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Phase 92 (D5): danh sách khoản dùng để LOẠI giao dịch khỏi thói quen chi tiêu / thu không đều / bất thường = khoản đang theo dõi + khoản đã
/// hoàn thành trong cửa sổ đang xét. Khoản trả góp vừa xong vẫn còn các lần trả trong lịch sử; bỏ nó khỏi danh sách loại trừ thì các lần trả đó
/// thành "chi linh hoạt" và dự báo vẫn trừ tiền dưới dạng khác (chiều thu: học bổng xong thành "thu không đều").
/// Danh sách dùng để CHIẾU (lịch đến hạn, có thu định kỳ hay không) vẫn chỉ là khoản đang theo dõi: đừng truyền danh sách này vào đó.
/// </summary>
public static class RecurringExclusions
{
    public static IReadOnlyList<RecurringItemView> With(
        IReadOnlyList<RecurringItemView> active, IEnumerable<RecurringItem> completed, DateOnly today, int windowDays)
    {
        var since = today.AddDays(-windowDays);
        var recent = completed
            .Where(c => c.CompletedAt is { } at && VietnamTime.DateOf(at) >= since)
            .Select(c => c.ToView())
            .ToList();
        return recent.Count == 0 ? active : [.. active, .. recent];
    }
}
