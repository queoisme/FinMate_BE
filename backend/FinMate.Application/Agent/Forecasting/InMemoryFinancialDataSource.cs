namespace FinMate.Application.Agent;

/// <summary>Dữ liệu giả lập cố định, bỏ qua <c>userId</c>: dùng cho bộ đánh giá (Task 12) và test. Không bao giờ được đăng ký ngoài môi trường Development.</summary>
public sealed class InMemoryFinancialDataSource(
    IReadOnlyList<long> incomeVnd,
    IReadOnlyList<long> spendVnd,
    long totalBalanceVnd,
    IncomeSegment? segmentOverride = null,
    IReadOnlyList<CategorySpendVnd>? categorySpend = null) : IFinancialDataSource
{
    public Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CategorySpendVnd>>((categorySpend ?? []).OrderByDescending(r => r.SpentVnd).ThenBy(r => r.Slug, StringComparer.Ordinal).ToList());

    public Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default)
    {
        var skip = Math.Max(0, incomeVnd.Count - months); // chỉ giữ `months` tháng gần nhất, như bản thật
        return Task.FromResult(new MonthlySeries(incomeVnd.Skip(skip).ToArray(), spendVnd.Skip(skip).ToArray()));
    }

    public Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(totalBalanceVnd);

    public Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(segmentOverride);
}
