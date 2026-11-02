namespace FinMate.Application.Forecasting;

/// <param name="ShortfallDate">Ngày đầu tiên số dư dự kiến xuống dưới ngưỡng.</param>
/// <param name="MaxDailySpendCents">Chi linh hoạt tối đa mỗi ngày từ hôm nay để số dư không xuống dưới ngưỡng (sơ đồ: "mức chi tối đa/ngày").</param>
public sealed record LowBalanceWarning(
    DateOnly ShortfallDate,
    long BalanceAtShortfallCents,
    DateOnly LowestDate,
    long LowestBalanceCents,
    long ThresholdCents,
    long MaxDailySpendCents,
    IReadOnlyList<CashflowCauseDto> Causes);

/// <summary>Trang 1 của sơ đồ: "Số dư dự kiến thấp hơn ngưỡng?" → xác định ngày thiếu tiền, nguyên nhân và mức chi tối đa/ngày.</summary>
public static class LowBalanceRule
{
    public static LowBalanceWarning? Evaluate(CashflowForecastDto forecast, long thresholdCents)
    {
        var firstBelow = forecast.Days.FirstOrDefault(d => d.ExpectedBalanceCents < thresholdCents);
        if (firstBelow is null)
        {
            return null;
        }

        var lowest = forecast.Days.MinBy(d => d.ExpectedBalanceCents)!;
        var untilLowest = forecast.Days.TakeWhile(d => d.Date <= lowest.Date).ToList();

        // Nếu mỗi ngày chi X thay cho mức dự kiến: số dư ngày thấp nhất = E + F − X·d ≥ T  ⇒  X ≤ (E + F − T) / d.
        var flexible = untilLowest.Sum(d => d.FlexibleSpendCents);
        var maxDaily = Math.Max(0, (lowest.ExpectedBalanceCents + flexible - thresholdCents) / untilLowest.Count);

        return new LowBalanceWarning(
            firstBelow.Date, firstBelow.ExpectedBalanceCents, lowest.Date, lowest.ExpectedBalanceCents, thresholdCents, maxDaily, forecast.MainCauses);
    }
}
