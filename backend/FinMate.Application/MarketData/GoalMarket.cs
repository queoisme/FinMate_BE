using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;

namespace FinMate.Application.MarketData;

/// <summary>Giá hiện tại của một chủ đề mục tiêu: giá bán ra đã xác nhận mới nhất của loại vàng mà chủ đề trỏ tới.</summary>
public sealed record GoalMarketQuote(GoalMarketTopic Topic, long PriceCents, DateTimeOffset At);

/// <summary>Biến động giá của một mục tiêu đang theo giá thị trường, để màn mục tiêu hiện "giá vàng +x% từ khi gắn".</summary>
public sealed record GoalMarketDto(
    string Topic,
    string Label,
    long BaselinePriceCents,
    DateTimeOffset BaselineAt,
    long LatestPriceCents,
    DateTimeOffset LatestAt,
    int ChangeBps,
    long AdjustedTargetCents);

public static class GoalMarketErrorCodes
{
    public const string TopicUnavailable = "MARKET_TOPIC_UNAVAILABLE";
}

/// <summary>Phép tính thuần cho mục tiêu theo giá thị trường. Số nguyên, không qua số thực.</summary>
public static class GoalMarketRules
{
    private const long TargetRounding = 100_000;

    /// <summary>Thay đổi so với giá mốc, điểm cơ bản (100 = 1%), làm tròn về 0.</summary>
    public static int ChangeBps(long baseline, long latest) => (int)((Int128)(latest - baseline) * 10_000 / baseline);

    /// <summary>Số tiền mục tiêu tương đương theo giá mới: <c>mục tiêu × giá mới / giá mốc</c>, làm tròn LÊN 100.000đ.</summary>
    public static long AdjustedTarget(long targetCents, long baseline, long latest)
    {
        var scaled = ((Int128)targetCents * latest + baseline - 1) / baseline;
        return (long)((scaled + TargetRounding - 1) / TargetRounding * TargetRounding);
    }

    /// <summary>Lùi bao nhiêu ngày, hoặc null nếu không phải báo. Chưa tính được ngày đạt (chưa góp lần nào) thì không báo.</summary>
    public static int? ShiftToReport(GoalForecast current, GoalForecast adjusted, int changeBps, GoalMarketAlertSettings settings)
    {
        if (changeBps < settings.MinChangePercent * 100 || current.LikelyDate is not { } before || adjusted.LikelyDate is not { } after
            || current.Status == GoalForecastStatus.Reached || adjusted.Status == GoalForecastStatus.Reached)
        {
            return null;
        }

        var shift = after.DayNumber - before.DayNumber;
        var turnedLate = current.Status == GoalForecastStatus.OnTrack && adjusted.Status == GoalForecastStatus.Behind;
        return shift >= settings.MinShiftDays || (turnedLate && shift > 0) ? shift : null;
    }

    public static string Title(GoalMarketTopic topic) => $"Giá {topic.Label} tăng ảnh hưởng mục tiêu của bạn";

    /// <summary>Mẫu cố định: chỉ có nhãn do admin đặt, tên mục tiêu do chính người dùng đặt, và các con số do code tính. Không có lời khuyên mua bán.</summary>
    public static string Body(GoalMarketTopic topic, string goalName, int changeBps, int shiftDays, bool turnedLate, long adjustedTarget)
    {
        var months = Math.Max(1, (shiftDays + 29) / 30);
        var delay = turnedLate && shiftDays < 30
            ? "mục tiêu có thể không kịp hạn"
            : $"ngày đạt dự kiến lùi khoảng {months} tháng";
        return $"Giá {topic.Label} đã tăng {changeBps / 100m:0.#}% kể từ khi mục tiêu \"{goalName}\" bắt đầu theo giá {topic.Label}. "
            + $"Với mức góp hiện tại, {delay}. Số tiền tương đương bây giờ khoảng {ForecastText.Money(adjustedTarget)}.";
    }
}

/// <summary>Đọc giá cho chủ đề mục tiêu, và gắn hoặc bỏ gắn mục tiêu với chủ đề.</summary>
public sealed class GoalMarketService(ISystemConfigProvider config, IMarketDataRepository market)
{
    /// <summary>Chủ đề đang bật và đã có giá xác nhận.</summary>
    public async Task<IReadOnlyList<GoalMarketQuote>> ListAsync(CancellationToken ct = default)
    {
        var settings = (await config.GetAsync(ct)).Document.MarketData;
        var prices = await market.ListLatestGoldPricesAsync(ct);
        return settings.GoalTopics
            .Where(t => t.Enabled)
            .Select(t => prices.FirstOrDefault(p => p.GoldKey == t.GoldKey) is { } p ? new GoalMarketQuote(t, p.SellCents, p.FirstSeenAt) : null)
            .OfType<GoalMarketQuote>()
            .ToList();
    }

    public async Task<GoalMarketQuote?> GetAsync(string topic, CancellationToken ct = default)
        => (await ListAsync(ct)).FirstOrDefault(q => q.Topic.Key == topic);

    /// <summary>
    /// <paramref name="topic"/>: null = giữ nguyên, chuỗi rỗng = bỏ gắn, mã = gắn (lấy giá hiện tại làm mốc).
    /// Mục tiêu đang gắn mà đổi số tiền thì lấy lại mốc theo giá hiện tại: số tiền mới đã tính theo giá đó.
    /// </summary>
    public async Task ApplyAsync(SavingGoal goal, string? topic, bool targetChanged, DateTimeOffset now, CancellationToken ct = default)
    {
        if (topic is { Length: 0 })
        {
            goal.MarketTopic = null;
            goal.MarketBaselineCents = null;
            goal.MarketBaselineAt = null;
            return;
        }

        var key = topic ?? goal.MarketTopic;
        var relink = topic is not null && topic != goal.MarketTopic;

        // Gửi lại đúng chủ đề cũ mà không đổi số tiền thì giữ mốc cũ, nếu không mức tăng từ trước tới giờ sẽ bị xóa mất.
        if (key is null || (!relink && !targetChanged))
        {
            return;
        }

        var quote = await GetAsync(key, ct);
        if (!relink)
        {
            // Đổi số tiền khi chủ đề tạm không có giá: không chặn việc sửa mục tiêu, giữ mốc cũ.
            if (quote is not null)
            {
                goal.MarketBaselineCents = quote.PriceCents;
                goal.MarketBaselineAt = now;
            }

            return;
        }

        if (goal.UserId is null || quote is null)
        {
            throw new BusinessRuleException(GoalMarketErrorCodes.TopicUnavailable, "Chủ đề giá này chưa có giá đã xác nhận, hoặc mục tiêu không phải mục tiêu cá nhân.");
        }

        goal.MarketTopic = quote.Topic.Key;
        goal.MarketBaselineCents = quote.PriceCents;
        goal.MarketBaselineAt = now;
    }

    /// <summary>Biến động giá của mục tiêu; null khi mục tiêu không theo giá hoặc chủ đề không còn giá.</summary>
    public async Task<GoalMarketDto?> DescribeAsync(SavingGoal goal, CancellationToken ct = default)
    {
        if (goal.MarketTopic is not { } topic || goal.MarketBaselineCents is not { } baseline || goal.MarketBaselineAt is not { } baselineAt
            || await GetAsync(topic, ct) is not { } quote)
        {
            return null;
        }

        return new GoalMarketDto(
            topic, quote.Topic.Label, baseline, baselineAt, quote.PriceCents, quote.At,
            GoalMarketRules.ChangeBps(baseline, quote.PriceCents), GoalMarketRules.AdjustedTarget(goal.TargetCents, baseline, quote.PriceCents));
    }
}
