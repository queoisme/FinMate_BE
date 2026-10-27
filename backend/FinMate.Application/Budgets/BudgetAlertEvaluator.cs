using FinMate.Domain.Entities;

namespace FinMate.Application.Budgets;

/// <summary>Ba mức cảnh báo: T1 (nhắc nhẹ), T2 (cảnh báo mạnh) — hai mức người dùng chỉnh được — và Over (vượt hạn mức, cố định 100%).</summary>
public enum BudgetAlertLevel
{
    T1,
    T2,
    Over,
}

/// <param name="Percent">Mốc đã chạm tính theo % hạn mức (T1/T2 của người dùng, hoặc 100). Hiển thị trong thông báo.</param>
/// <param name="Body">Đã chèn sẵn tên danh mục (hoặc "toàn bộ chi tiêu" với budget tổng).</param>
/// <param name="Level">Mức đã chạm. Dùng để đóng cờ — KHÔNG dùng <paramref name="Percent"/>, vì T1/T2 đổi theo người dùng.</param>
public record BudgetAlert(Guid UserId, int Percent, string Title, string Body, BudgetAlertLevel Level);

/// <summary>
/// Quyết định một chu kỳ ngân sách vừa chạm mức cảnh báo nào.
///
/// Tách khỏi <c>BudgetAlertJob</c> vì nay có HAI đường gọi: đường tức thì sau mỗi giao dịch
/// (docx Flow 2 mục 2a — "kích hoạt kiểm tra" ngay khi phát sinh) và job theo giờ làm lưới
/// vét. Để logic mốc nằm trong job thì đường tức thì phải chép lại nó, và hai bản chép sẽ
/// lệch nhau vào lúc không ai để ý.
///
/// T1 và T2 do <see cref="AlertThresholds"/> cung cấp (theo phương pháp và tuỳ chỉnh của từng người dùng); mức Over luôn là 100%.
/// </summary>
public static class BudgetAlertEvaluator
{
    private const int OverPercent = 100;

    /// <summary>
    /// Các mức theo thứ tự GIẢM DẦN — bắt buộc, vì thuật toán lấy mức cao nhất đã chạm rồi
    /// đóng mọi mức thấp hơn.
    /// </summary>
    private static readonly Flag[] Flags =
    [
        new(BudgetAlertLevel.Over, p => p.AlertOverSentAt, (p, at) => p.AlertOverSentAt = at),
        new(BudgetAlertLevel.T2, p => p.AlertT2SentAt, (p, at) => p.AlertT2SentAt = at),
        new(BudgetAlertLevel.T1, p => p.AlertT1SentAt, (p, at) => p.AlertT1SentAt = at),
    ];

    /// <summary>
    /// Mức cao nhất vừa chạm mà chưa từng gửi, hoặc null. THUẦN — không đụng gì vào
    /// <paramref name="period"/>.
    ///
    /// Chỉ trả mức CAO NHẤT. Một giao dịch có thể nhảy từ dưới T1 lên quá 100%; gửi cả ba
    /// thông báo là spam, mà gửi mức thấp sau khi đã vượt 100% thì sai hẳn thông điệp.
    /// </summary>
    public static BudgetAlert? Evaluate(BudgetPeriod period, Guid userId, string scope, AlertThresholds thresholds)
    {
        if (period.LimitCents <= 0)
        {
            return null;
        }

        foreach (var threshold in Thresholds(thresholds))
        {
            if (period.SpentCents * 100 < period.LimitCents * threshold.Percent)
            {
                continue;
            }

            return FlagOf(threshold.Level).GetSentAt(period) is null
                ? new BudgetAlert(userId, threshold.Percent, threshold.Title, threshold.Body(scope), threshold.Level)
                : null;
        }

        return null;
    }

    /// <summary>
    /// Đóng mức vừa gửi và mọi mức THẤP HƠN, để lần sau không bắn ngược trở lại.
    ///
    /// Tách khỏi <see cref="Evaluate"/> có lý do: chỉ được đánh dấu khi cảnh báo THẬT SỰ được
    /// gửi. Đánh dấu cho một người đã tắt thông báo ngân sách là mất hẳn mức đó — họ bật lại
    /// thì cờ đã nói "đã gửi" và không ai gửi lại nữa.
    /// </summary>
    public static void MarkSent(BudgetPeriod period, BudgetAlert alert, DateTimeOffset now)
    {
        var index = Array.FindIndex(Flags, f => f.Level == alert.Level);
        if (index < 0)
        {
            return;
        }

        foreach (var flag in Flags[index..])
        {
            if (flag.GetSentAt(period) is null)
            {
                flag.SetSentAt(period, now);
            }
        }

        period.UpdatedAt = now;
    }

    /// <summary>
    /// Mở lại các mức mà chi tiêu hiện KHÔNG còn chạm tới (sau khi nâng hạn mức), để cảnh báo bắn lại nếu người dùng chạm nó lần nữa
    /// trong cùng chu kỳ. Xét độc lập từng mức: nâng hạn mức có thể đưa người dùng xuống dưới T2 mà vẫn còn trên T1.
    /// </summary>
    public static void ReopenBelow(BudgetPeriod period, AlertThresholds thresholds)
    {
        foreach (var threshold in Thresholds(thresholds))
        {
            if (period.SpentCents * 100 < period.LimitCents * threshold.Percent)
            {
                FlagOf(threshold.Level).SetSentAt(period, null);
            }
        }
    }

    private static Flag FlagOf(BudgetAlertLevel level) => Flags.Single(f => f.Level == level);

    private static Threshold[] Thresholds(AlertThresholds requested)
    {
        // Ngưỡng sai (không do provider tạo) không được làm hỏng đường cảnh báo: dùng mặc định.
        var t = AlertThresholds.IsValid(requested) ? requested : AlertThresholds.Default;
        return
        [
            new(BudgetAlertLevel.Over, OverPercent, "Vượt hạn mức chi tiêu",
                scope => $"Bạn đã vượt hạn mức {scope} trong chu kỳ này."),
            new(BudgetAlertLevel.T2, t.T2Pct, "Sắp cạn hạn mức chi tiêu",
                scope => $"Bạn đã dùng hơn {t.T2Pct}% hạn mức {scope} trong chu kỳ này."),
            new(BudgetAlertLevel.T1, t.T1Pct, $"Đã dùng quá {t.T1Pct}% hạn mức",
                scope => $"Bạn đã dùng hơn {t.T1Pct}% hạn mức {scope} trong chu kỳ này."),
        ];
    }

    private sealed record Threshold(BudgetAlertLevel Level, int Percent, string Title, Func<string, string> Body);

    private sealed record Flag(
        BudgetAlertLevel Level,
        Func<BudgetPeriod, DateTimeOffset?> GetSentAt,
        Action<BudgetPeriod, DateTimeOffset?> SetSentAt);
}
