using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;

namespace FinMate.Application.Kpi;

public interface IKpiSummaryService
{
    Task<KpiSummaryDto> GetSummaryAsync(Guid actorId, DateOnly? from, DateOnly? to, string? granularity, CancellationToken ct = default);
}

/// <summary>
/// Dashboard KPI sản phẩm (Phase 94, bản gọn). Tính khi gọi từ các bảng sẵn có, cache 10 phút theo tham số.
/// Quyền <see cref="OpsCapability.ViewProductKpi"/> đọc từ DB mỗi lần; 2FA do policy của controller.
/// </summary>
public sealed class KpiSummaryService : IKpiSummaryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>Nguồn giao dịch luôn có mặt trong <c>bySource</c> (0 nếu không có), theo dạng lưu trong DB.</summary>
    private static readonly string[] Sources = Enum.GetNames<TransactionSource>().Select(n => n.ToLowerInvariant()).ToArray();

    private const string IgnoredStatus = "ignored";
    private const string DuplicateStatus = "duplicate";

    private readonly IOpsAccessService _access;
    private readonly IKpiRepository _repository;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;

    public KpiSummaryService(IOpsAccessService access, IKpiRepository repository, ISystemConfigProvider config, ICacheService cache)
    {
        _access = access;
        _repository = repository;
        _config = config;
        _cache = cache;
    }

    public async Task<KpiSummaryDto> GetSummaryAsync(Guid actorId, DateOnly? from, DateOnly? to, string? granularity, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ViewProductKpi, ct);
        var now = DateTimeOffset.UtcNow;
        var today = VietnamTime.DateOf(now);
        var (start, end, grain) = KpiPeriods.Validate(from, to, granularity, today);
        var minCellSize = (await _config.GetAsync(ct)).Document.Kpi.MinCellSize;

        var cacheKey = $"kpi:summary:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}:{grain}:{minCellSize}";
        if (await _cache.GetAsync<KpiSummaryDto>(cacheKey, ct) is { } cached)
        {
            return cached;
        }

        var summary = await BuildAsync(start, end, grain, today, now, minCellSize, ct);
        await _cache.SetAsync(cacheKey, summary, CacheTtl, ct);
        return summary;
    }

    private async Task<KpiSummaryDto> BuildAsync(
        DateOnly start, DateOnly end, KpiGranularity grain, DateOnly today, DateTimeOffset now, int minCellSize, CancellationToken ct)
    {
        var periods = KpiPeriods.Split(start, end, grain, today);
        var daily = await _repository.GetDailyCountsAsync(periods[0].StartUtc, periods[^1].EndUtc, ct);
        var transacting = await _repository.CountTransactingUsersAsync(periods.Select(p => new KpiTimeWindow(p.StartUtc, p.EndUtc)).ToList(), ct);
        var paying = await _repository.CountPayingAsync(periods.Select(p => p.EndUtc < now ? p.EndUtc : now).ToList(), ct);
        var features = await _repository.CountFeatureUsersAsync(ct);

        var series = new KpiSeries(daily);
        var items = periods.Select((p, i) => BuildPeriod(p, series, transacting[i], paying[i], minCellSize)).ToList();
        return new KpiSummaryDto(
            grain.ToString().ToLowerInvariant(),
            minCellSize,
            new KpiFeatureUsersDto(features.Budgets, features.Goals, features.Loans, features.Groups, now),
            new KpiNotesDto(ManualIncludesChat: true),
            items);
    }

    private static KpiPeriodDto BuildPeriod(KpiPeriod p, KpiSeries s, long transactingUsers, KpiPayingSnapshot paying, int minCellSize)
    {
        var bySource = s.SumByKey(KpiMetrics.Transactions, p);
        var byStatus = s.SumByKey(KpiMetrics.Notifications, p);
        var received = byStatus.Values.Sum();
        var ignored = byStatus.GetValueOrDefault(IgnoredStatus);
        var duplicate = byStatus.GetValueOrDefault(DuplicateStatus);
        var converted = s.Sum(KpiMetrics.NotificationsConverted, p);

        return new KpiPeriodDto(
            p.Start,
            p.End,
            p.Partial,
            s.Sum(KpiMetrics.Signups, p),
            s.Sum(KpiMetrics.EmailVerified, p),
            s.Sum(KpiMetrics.OnboardingCompleted, p),
            s.Sum(KpiMetrics.MethodChosen, p),
            s.Sum(KpiMetrics.DeletionRequests, p),
            transactingUsers,
            new KpiTransactionsDto(bySource.Values.Sum(), KpiMath.MaskCells(bySource, Sources, minCellSize)),
            new KpiNotificationsDto(received, ignored, duplicate, converted, KpiMath.ConversionRate(converted, received, ignored, duplicate)),
            new KpiSubscriptionsDto(
                paying.Total,
                KpiMath.MaskCells(paying.ByPlan, [], minCellSize),
                s.Sum(KpiMetrics.PaidOrders, p),
                s.Sum(KpiMetrics.Revenue, p),
                s.Sum(KpiMetrics.NeedsReview, p)));
    }

    /// <summary>Các dòng theo ngày, tra theo chỉ số (và khoá) để cộng trong một kỳ.</summary>
    private sealed class KpiSeries
    {
        private readonly ILookup<string, KpiDailyRow> _byMetric;

        public KpiSeries(IEnumerable<KpiDailyRow> rows)
        {
            _byMetric = rows.ToLookup(r => r.Metric, StringComparer.Ordinal);
        }

        public long Sum(string metric, KpiPeriod period)
            => _byMetric[metric].Where(r => period.Contains(r.Day)).Sum(r => r.Value);

        public IReadOnlyDictionary<string, long> SumByKey(string metric, KpiPeriod period)
            => _byMetric[metric]
                .Where(r => r.Key is not null && period.Contains(r.Day))
                .GroupBy(r => r.Key!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Value), StringComparer.Ordinal);
    }
}
