namespace FinMate.Application.Agent;

/// <summary>Lịch sử thu nhập của MỘT người dùng → <see cref="IncomeProfile"/>. Mọi dữ liệu đi qua <see cref="IFinancialDataSource"/>.</summary>
public sealed class IncomeHistory(IFinancialDataSource source)
{
    public async Task<IncomeProfile> GetAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var series = await source.GetMonthlySeriesAsync(userId, AgentLimits.HistoryMonths, now, ct);
        var segmentOverride = await source.GetIncomeSegmentOverrideAsync(userId, ct);
        return IntervalForecaster.BuildProfile(series.IncomeVnd, segmentOverride);
    }
}
