namespace FinMate.Application.Agent;

/// <summary>
/// Nguồn dữ liệu mặc định là bản thật; <see cref="Use"/> đổi sang dữ liệu giả lập CHO RIÊNG request này (đăng ký theo phạm vi request). Chỉ <c>AgentEvalController</c> gọi
/// <see cref="Use"/>, và controller đó chỉ tồn tại khi chạy Development với AGENT_EVAL_ENABLED, nên ở production nguồn thật không bao giờ bị thay.
/// </summary>
public sealed class SwitchableFinancialDataSource(RepositoryFinancialDataSource real) : IFinancialDataSource
{
    private IFinancialDataSource _current = real;

    public void Use(IFinancialDataSource source) => _current = source;

    public Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default)
        => _current.GetMonthlySeriesAsync(userId, months, now, ct);

    public Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default) => _current.GetTotalBalanceAsync(userId, ct);

    public Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default) => _current.GetIncomeSegmentOverrideAsync(userId, ct);
}
