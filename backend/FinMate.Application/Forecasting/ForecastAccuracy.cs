using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Tên phương pháp ghi vào mỗi bản chụp. Đổi cách tính (trung bình cắt, dải bootstrap...) thì đặt tên mới để so sánh trước và sau.</summary>
public static class ForecastMethods
{
    public const string MedianBand = "median_p25p75";
}

/// <summary>
/// Biến một kết quả dự báo thành các bản chụp để đo sau này. Hàm thuần. Mốc nhìn trước 7, 14, 30 ngày: ngắn đủ để có kết quả thật sớm,
/// 30 ngày là tầm mặc định của dự báo. Mốc so sánh B0 (chỉ khoản định kỳ) lấy từ chính các sự kiện của dự báo nên không cần tính lại.
/// </summary>
public static class ForecastSnapshotBuilder
{
    public static readonly IReadOnlyList<int> Horizons = [7, 14, 30];

    public static IReadOnlyList<ForecastSnapshot> Build(Guid userId, CashflowForecastDto forecast, int configVersion, DateTimeOffset now)
    {
        var snapshots = new List<ForecastSnapshot>();
        foreach (var horizon in Horizons)
        {
            var date = forecast.From.AddDays(horizon);
            var index = -1;
            for (var i = 0; i < forecast.Days.Count; i++)
            {
                if (forecast.Days[i].Date == date)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                continue;
            }

            var day = forecast.Days[index];
            var recurringOnly = forecast.StartBalanceCents + forecast.Days.Take(index + 1).Sum(d => d.Events.Sum(e => e.AmountCents));
            snapshots.Add(new ForecastSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OriginDate = forecast.From,
                HorizonDays = horizon,
                HorizonDate = date,
                Method = ForecastMethods.MedianBand,
                ConfigVersion = configVersion,
                StartBalanceCents = forecast.StartBalanceCents,
                PredictedCents = day.ExpectedBalanceCents,
                ConservativeCents = day.LowBalanceCents,
                OptimisticCents = day.HighBalanceCents,
                RecurringOnlyCents = recurringOnly,
                Confidence = forecast.Confidence,
                CreatedAt = now,
            });
        }

        return snapshots;
    }
}

/// <summary>
/// Số dư thực tế cuối một ngày = số dư lúc chụp + thu chi thật từ ngày gốc tới ngày đó, cùng định nghĩa với dự báo: chỉ ví được tính, không
/// gồm chuyển khoản nội bộ và cho vay/mượn (bộ đọc giao dịch của dự báo đã loại sẵn). Giao dịch user loại khỏi dự báo vẫn tính vì tiền đã đổi thật.
/// Job chụp chạy sáng sớm nên giao dịch của chính ngày gốc gần như chưa có và không bị tính hai lần.
/// </summary>
public static class ForecastActuals
{
    public static long NetFlow(IEnumerable<ForecastTransaction> transactions, IReadOnlySet<Guid> accountIds, DateOnly from, DateOnly to)
        => transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= from && t.Date <= to)
            .Sum(t => t.Type == TransactionType.Credit ? t.AmountCents : -t.AmountCents);
}

/// <param name="Confidence"><c>all</c>, <c>low</c> hoặc <c>normal</c>: kết quả tách theo nhóm khởi động lạnh (ít dữ liệu) và đủ dữ liệu.</param>
/// <param name="MaeCents">Sai số tuyệt đối trung bình của dự báo (đồng): "lệch trung bình X đồng".</param>
/// <param name="MaeRecurringOnlyCents">Cùng chỉ số nếu chỉ dùng khoản định kỳ (mốc B0 kiểu Simplifi/Monzo).</param>
/// <param name="MaeHoldCents">Cùng chỉ số nếu giả định số dư không đổi (mốc so sánh thấp nhất).</param>
/// <param name="CoveragePercent">Tỉ lệ số dư thật nằm trong dải. Dải đúng nghĩa P25–P75 thì phải gần 50%; cao hơn nhiều nghĩa là dải quá rộng.</param>
/// <param name="RatioVsHold">MAE của dự báo chia MAE của "số dư không đổi". Nhỏ hơn 1 là hơn mốc đó.</param>
public sealed record AccuracyRowDto(
    int HorizonDays,
    string Confidence,
    int Samples,
    long MaeCents,
    long MedianAbsErrorCents,
    long MaeRecurringOnlyCents,
    long MaeHoldCents,
    double? CoveragePercent,
    long MeanBandWidthCents,
    double? RatioVsHold,
    double? RatioVsRecurringOnly);

/// <summary>Tính chỉ số độ chính xác từ các bản chụp đã có số thực tế. Hàm thuần.</summary>
public static class ForecastAccuracyCalculator
{
    public static IReadOnlyList<AccuracyRowDto> Compute(IEnumerable<ForecastSnapshot> filled)
    {
        var data = filled.Where(s => s.ActualCents is not null).ToList();
        var rows = new List<AccuracyRowDto>();
        foreach (var horizon in ForecastSnapshotBuilder.Horizons)
        {
            var atHorizon = data.Where(s => s.HorizonDays == horizon).ToList();
            foreach (var (label, group) in new[]
                     {
                         ("all", atHorizon),
                         ("low", atHorizon.Where(s => s.Confidence == "low").ToList()),
                         ("normal", atHorizon.Where(s => s.Confidence != "low").ToList()),
                     })
            {
                if (group.Count > 0)
                {
                    rows.Add(Row(horizon, label, group));
                }
            }
        }

        return rows;
    }

    private static AccuracyRowDto Row(int horizon, string confidence, IReadOnlyList<ForecastSnapshot> group)
    {
        var errors = group.Select(s => Math.Abs(s.ActualCents!.Value - s.PredictedCents)).ToList();
        var recurringErrors = group.Select(s => Math.Abs(s.ActualCents!.Value - s.RecurringOnlyCents)).ToList();
        var holdErrors = group.Select(s => Math.Abs(s.ActualCents!.Value - s.StartBalanceCents)).ToList();
        var mae = Mean(errors);
        var maeRecurring = Mean(recurringErrors);
        var maeHold = Mean(holdErrors);
        var inside = group.Count(s =>
            s.ActualCents!.Value >= Math.Min(s.ConservativeCents, s.OptimisticCents) && s.ActualCents.Value <= Math.Max(s.ConservativeCents, s.OptimisticCents));
        var width = group.Sum(s => Math.Abs(s.OptimisticCents - s.ConservativeCents)) / group.Count;

        return new AccuracyRowDto(
            horizon, confidence, group.Count, mae, Median(errors), maeRecurring, maeHold,
            Math.Round(100.0 * inside / group.Count, 1), width,
            maeHold > 0 ? Math.Round((double)mae / maeHold, 2) : null,
            maeRecurring > 0 ? Math.Round((double)mae / maeRecurring, 2) : null);
    }

    private static long Mean(IReadOnlyList<long> values) => values.Count == 0 ? 0 : (long)Math.Round(values.Average(), MidpointRounding.AwayFromZero);

    private static long Median(IReadOnlyList<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
