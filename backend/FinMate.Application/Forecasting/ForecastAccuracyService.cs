using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Forecasting;

public sealed record ForecastCountDto(string Key, int Count);

/// <param name="FeedbackByCause">Chỉ các phản hồi Staff đã gán nguyên nhân.</param>
public sealed record ForecastAccuracyReportDto(
    int WindowDays,
    DateOnly From,
    DateOnly To,
    string Method,
    int SnapshotsFilled,
    int SnapshotsPending,
    IReadOnlyList<AccuracyRowDto> Rows,
    int FeedbackTotal,
    int FeedbackNew,
    IReadOnlyList<ForecastCountDto> FeedbackByReason,
    IReadOnlyList<ForecastCountDto> FeedbackByCause);

public interface IForecastAccuracyService
{
    /// <summary>Chụp dự báo của mọi người dùng có hoạt động gần đây (mỗi người một lần mỗi ngày). Trả số bản chụp mới.</summary>
    Task<int> CaptureAsync(DateOnly today, CancellationToken ct = default);

    /// <summary>Điền số dư thực tế cho các bản chụp đã tới ngày của mốc. Trả số bản chụp vừa điền.</summary>
    Task<int> FillActualsAsync(DateOnly today, CancellationToken ct = default);

    /// <summary>Chỉ số độ chính xác trong cửa sổ <paramref name="windowDays"/> ngày gần nhất (7–180). Cần quyền xem giám sát (OpsManager trở lên); số liệu tổng hợp, không có định danh người dùng.</summary>
    Task<ForecastAccuracyReportDto> GetReportAsync(Guid actorId, int windowDays, CancellationToken ct = default);
}

public class ForecastAccuracyService(
    IReportRepository reports,
    IForecastService forecast,
    ISystemConfigProvider config,
    IForecastAccuracyRepository repository,
    IForecastDataRepository data,
    IFinancialAccountRepository accounts,
    IOpsAccessService access,
    ILogger<ForecastAccuracyService> logger) : IForecastAccuracyService
{
    private const int MaxFillBatch = 5000;
    private const int DefaultWindowDays = 30;
    private const int MinWindowDays = 7;
    private const int MaxWindowDays = 180;

    public async Task<int> CaptureAsync(DateOnly today, CancellationToken ct = default)
    {
        var snapshot = await config.GetAsync(ct);
        var userIds = await reports.GetUserIdsWithRecentActivityAsync(today.AddDays(-snapshot.Document.Forecast.HistoryWindowDays), today, ct);
        var horizon = ForecastSnapshotBuilder.Horizons.Max();
        var created = 0;

        foreach (var userId in userIds)
        {
            try
            {
                if (await repository.HasSnapshotsAsync(userId, today, ct))
                {
                    continue;
                }

                var cashflow = await forecast.GetCashflowAsync(userId, null, today.AddDays(horizon), ct);
                if (cashflow.StartBalanceCents == 0 && cashflow.Days.All(d => d.Events.Count == 0))
                {
                    continue;   // không có ví được tính và không có khoản định kỳ: không có gì để đánh giá
                }

                var rows = ForecastSnapshotBuilder.Build(userId, cashflow, snapshot.Version, DateTimeOffset.UtcNow);
                if (rows.Count == 0)
                {
                    continue;
                }

                await repository.AddSnapshotsAsync(rows, ct);
                await repository.SaveChangesAsync(ct);
                created += rows.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Forecast snapshot failed for user {UserId}", userId);   // lỗi của một người không chặn những người còn lại
            }
        }

        logger.LogInformation("Forecast snapshots: {Users} users checked, {Created} rows created", userIds.Count, created);
        return created;
    }

    public async Task<int> FillActualsAsync(DateOnly today, CancellationToken ct = default)
    {
        var due = await repository.ListAwaitingActualAsync(today, MaxFillBatch, ct);
        var filled = 0;

        foreach (var group in due.GroupBy(s => s.UserId))
        {
            try
            {
                var included = (await accounts.GetListByUserAsync(group.Key, ct)).Where(a => a.IncludeInForecast).Select(a => a.Id).ToHashSet();
                var transactions = await data.GetRawTransactionsAsync(group.Key, group.Min(s => s.OriginDate), group.Max(s => s.HorizonDate), ct);
                var now = DateTimeOffset.UtcNow;
                foreach (var s in group)
                {
                    s.ActualCents = s.StartBalanceCents + ForecastActuals.NetFlow(transactions, included, s.OriginDate, s.HorizonDate);
                    s.ActualFilledAt = now;
                    filled++;
                }

                await repository.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Forecast actuals failed for user {UserId}", group.Key);
            }
        }

        logger.LogInformation("Forecast actuals filled: {Filled} snapshots", filled);
        return filled;
    }

    public async Task<ForecastAccuracyReportDto> GetReportAsync(Guid actorId, int windowDays, CancellationToken ct = default)
    {
        await access.RequireAsync(actorId, OpsCapability.ViewMonitoring, ct);
        var window = windowDays <= 0 ? DefaultWindowDays : Math.Clamp(windowDays, MinWindowDays, MaxWindowDays);
        var today = VietnamTime.Today();
        var from = today.AddDays(-window);

        var filled = await repository.ListFilledSinceAsync(from, ct);
        var pending = await repository.CountPendingAsync(today, ct);
        var feedback = await repository.ListFeedbackSinceAsync(new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), VietnamTime.Offset), ct);

        return new ForecastAccuracyReportDto(
            window, from, today, ForecastMethods.MedianBand, filled.Count, pending,
            ForecastAccuracyCalculator.Compute(filled),
            feedback.Count,
            feedback.Count(f => f.Status == Domain.Entities.ForecastFeedbackStatuses.New),
            Count(feedback.Select(f => f.Reason)),
            Count(feedback.Where(f => f.Cause is not null).Select(f => f.Cause!)));
    }

    private static IReadOnlyList<ForecastCountDto> Count(IEnumerable<string> keys)
        => keys.GroupBy(k => k).Select(g => new ForecastCountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Key).ToList();
}
