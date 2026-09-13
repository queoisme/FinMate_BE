using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

/// <param name="Percent">Mốc đã chạm: 70, 90 hay 100. Dùng để đóng cờ, không hiển thị.</param>
/// <param name="Body">Đã chèn sẵn tên danh mục (hoặc "toàn bộ chi tiêu" với budget tổng).</param>
public record BudgetAlert(Guid UserId, int Percent, string Title, string Body);

/// <summary>
/// Quyết định một chu kỳ ngân sách vừa chạm mốc cảnh báo nào.
///
/// Tách khỏi <c>BudgetAlertJob</c> vì nay có HAI đường gọi: đường tức thì sau mỗi giao dịch
/// (docx Flow 2 mục 2a — "kích hoạt kiểm tra" ngay khi phát sinh) và job theo giờ làm lưới
/// vét. Để logic mốc nằm trong job thì đường tức thì phải chép lại nó, và hai bản chép sẽ
/// lệch nhau vào lúc không ai để ý.
/// </summary>
public static class BudgetAlertEvaluator
{
    /// <summary>
    /// Các mốc theo thứ tự GIẢM DẦN — bắt buộc, vì thuật toán lấy mốc cao nhất đã chạm rồi
    /// đóng mọi mốc thấp hơn.
    /// </summary>
    private static readonly Threshold[] Thresholds =
    [
        new(100, "Vượt hạn mức chi tiêu", scope => $"Bạn đã vượt hạn mức {scope} trong chu kỳ này.",
            p => p.Alert100SentAt, (p, at) => p.Alert100SentAt = at),
        new(90, "Sắp cạn hạn mức chi tiêu", scope => $"Bạn đã dùng hơn 90% hạn mức {scope} trong chu kỳ này.",
            p => p.Alert90SentAt, (p, at) => p.Alert90SentAt = at),
        new(70, "Đã dùng quá 70% hạn mức", scope => $"Bạn đã dùng hơn 70% hạn mức {scope} trong chu kỳ này.",
            p => p.Alert70SentAt, (p, at) => p.Alert70SentAt = at),
    ];

    /// <summary>
    /// Mốc cao nhất vừa chạm mà chưa từng gửi, hoặc null. THUẦN — không đụng gì vào
    /// <paramref name="period"/>.
    ///
    /// Chỉ trả mốc CAO NHẤT. Một giao dịch có thể nhảy từ dưới 70% lên quá 100%; gửi cả ba
    /// thông báo là spam, mà gửi mốc thấp sau khi đã vượt 100% thì sai hẳn thông điệp.
    /// </summary>
    public static BudgetAlert? Evaluate(BudgetPeriod period, Guid userId, string scope)
    {
        if (period.LimitCents <= 0)
        {
            return null;
        }

        var reached = Array.FindIndex(
            Thresholds, t => period.SpentCents * 100 >= period.LimitCents * t.Percent);
        if (reached < 0)
        {
            return null;
        }

        var highest = Thresholds[reached];
        return highest.GetSentAt(period) is null
            ? new BudgetAlert(userId, highest.Percent, highest.Title, highest.Body(scope))
            : null;
    }

    /// <summary>
    /// Đóng mốc vừa gửi và mọi mốc THẤP HƠN, để lần sau không bắn ngược trở lại.
    ///
    /// Tách khỏi <see cref="Evaluate"/> có lý do: chỉ được đánh dấu khi cảnh báo THẬT SỰ được
    /// gửi. Đánh dấu cho một người đã tắt thông báo ngân sách là mất hẳn mốc đó — họ bật lại
    /// thì cờ đã nói "đã gửi" và không ai gửi lại nữa.
    /// </summary>
    public static void MarkSent(BudgetPeriod period, BudgetAlert alert, DateTimeOffset now)
    {
        var index = Array.FindIndex(Thresholds, t => t.Percent == alert.Percent);
        if (index < 0)
        {
            return;
        }

        foreach (var threshold in Thresholds[index..])
        {
            if (threshold.GetSentAt(period) is null)
            {
                threshold.SetSentAt(period, now);
            }
        }

        period.UpdatedAt = now;
    }

    private sealed record Threshold(
        int Percent,
        string Title,
        Func<string, string> Body,
        Func<BudgetPeriod, DateTimeOffset?> GetSentAt,
        Action<BudgetPeriod, DateTimeOffset> SetSentAt);
}
