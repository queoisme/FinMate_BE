using FinMate.Application.Reports;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>Ngưỡng phát hiện bất thường (thay các mã BR-11.x chưa có định nghĩa), lấy từ cấu hình hệ thống <c>forecast</c>.</summary>
public sealed record AnomalyRules(
    int MinCategorySamples,
    int MinMerchantSamples,
    int RatioPercent,
    int HighRatioPercent,
    long MinAmountCents,
    int DuplicateWindowMinutes,
    int NormalTolerancePercent)
{
    public static AnomalyRules From(Admin.SystemConfig.ForecastSettings s) => new(
        s.AnomalyMinCategorySamples, s.AnomalyMinMerchantSamples, s.AnomalyRatioPercent, s.AnomalyHighRatioPercent,
        s.AnomalyMinAmountCents, s.DuplicateWindowMinutes, s.NormalFeedbackTolerancePercent);
}

/// <summary>Một khoản user đã xác nhận "bình thường" trước đây (dữ liệu cho BR-11.4).</summary>
public sealed record PriorNormal(Guid? CategoryId, string? CounterpartyKey, long AmountCents);

public sealed record AnomalyFinding(AnomalyKind Kind, AnomalySeverity Severity, long TypicalCents, int RatioPercent);

/// <summary>
/// Phát hiện chi tiêu bất thường (Flow 6, trang 9). Hàm thuần: nhận một khoản chi và lịch sử, trả loại + mức độ hoặc null.
/// Thứ tự xét theo sơ đồ: trùng lặp → so với người nhận → so với danh mục; rồi "đã xác nhận bình thường trước đó?" (BR-11.4) chặn lại.
/// Khoản khớp khoản định kỳ đang theo dõi không xét ở đây: đổi giá của chúng đã có cảnh báo riêng (đợt 2), và chúng cũng không được làm mẫu so sánh.
/// </summary>
public static class AnomalyDetector
{
    private const int DuplicateRatioPercent = 100;

    /// <param name="history">Khoản chi trong cửa sổ so sánh; có thể chứa chính <paramref name="candidate"/> (sẽ bị bỏ khi làm mẫu).</param>
    public static AnomalyFinding? Evaluate(
        ForecastTransaction candidate,
        IReadOnlyList<ForecastTransaction> history,
        IReadOnlyList<RecurringItemView> activeItems,
        IReadOnlyList<PriorNormal> priorNormals,
        ForecastRules forecastRules,
        AnomalyRules rules)
    {
        if (candidate.Type != TransactionType.Debit
            || candidate.ExcludedFromForecast
            || candidate.AmountCents < rules.MinAmountCents
            || RecurringMatcher.MatchesAny(candidate, activeItems, forecastRules))
        {
            return null;
        }

        var finding = FindDuplicate(candidate, history, rules) ?? FindSpike(candidate, history, activeItems, forecastRules, rules);
        return finding is null || ConfirmedNormalBefore(candidate, priorNormals, rules) ? null : finding;
    }

    /// <summary>Cùng ví, cùng số tiền, cùng nơi nhận (hoặc cùng danh mục khi không có tên) trong vài phút. Chỉ khoản SAU của cặp bị đánh dấu.</summary>
    private static AnomalyFinding? FindDuplicate(ForecastTransaction candidate, IReadOnlyList<ForecastTransaction> history, AnomalyRules rules)
    {
        var window = TimeSpan.FromMinutes(rules.DuplicateWindowMinutes);
        var hasEarlierTwin = history.Any(t => t.Id != candidate.Id
            && t.Type == TransactionType.Debit
            && t.AccountId == candidate.AccountId
            && t.AmountCents == candidate.AmountCents
            && SamePayee(t, candidate)
            && IsEarlier(t, candidate)
            && candidate.TransactedAt - t.TransactedAt <= window);
        return hasEarlierTwin
            ? new AnomalyFinding(AnomalyKind.Duplicate, AnomalySeverity.High, candidate.AmountCents, DuplicateRatioPercent)
            : null;
    }

    private static AnomalyFinding? FindSpike(
        ForecastTransaction candidate,
        IReadOnlyList<ForecastTransaction> history,
        IReadOnlyList<RecurringItemView> activeItems,
        ForecastRules forecastRules,
        AnomalyRules rules)
    {
        var samples = history
            .Where(t => t.Id != candidate.Id
                && t.Type == TransactionType.Debit
                && !t.ExcludedFromForecast
                && t.TransactedAt <= candidate.TransactedAt
                && !RecurringMatcher.MatchesAny(t, activeItems, forecastRules))
            .ToList();

        if (candidate.CounterpartyKey is not null)
        {
            var merchant = Amounts(samples.Where(t => t.CounterpartyKey == candidate.CounterpartyKey));
            if (merchant.Count >= rules.MinMerchantSamples
                && Spike(candidate.AmountCents, merchant, AnomalyKind.MerchantSpike, rules, requireOutlier: false) is { } byMerchant)
            {
                return byMerchant;
            }
        }

        if (candidate.CategoryId is null)
        {
            return null;
        }

        var category = Amounts(samples.Where(t => t.CategoryId == candidate.CategoryId));
        return category.Count >= rules.MinCategorySamples
            ? Spike(candidate.AmountCents, category, AnomalyKind.CategorySpike, rules, requireOutlier: true)
            : null;
    }

    /// <summary>
    /// Gấp ít nhất <see cref="AnomalyRules.RatioPercent"/> trung vị. Với danh mục còn phải vượt ngưỡng outlier (trung vị + 3·MAD): một danh mục vốn dao động
    /// rộng (mua sắm) có trung vị thấp nhưng khoản lớn là chuyện thường.
    /// </summary>
    private static AnomalyFinding? Spike(long amount, IReadOnlyList<double> samples, AnomalyKind kind, AnomalyRules rules, bool requireOutlier)
    {
        var median = (long)Math.Round(Statistics.Median(samples), MidpointRounding.AwayFromZero);
        if (median <= 0)
        {
            return null;
        }

        var ratio = (int)Math.Min(int.MaxValue, amount * 100 / median);
        if (ratio < rules.RatioPercent || (requireOutlier && amount <= Statistics.UpperOutlierBound(samples)))
        {
            return null;
        }

        var severity = ratio >= rules.HighRatioPercent ? AnomalySeverity.High : AnomalySeverity.Normal;
        return new AnomalyFinding(kind, severity, median, ratio);
    }

    /// <summary>BR-11.4: cùng danh mục + người nhận đã được xác nhận bình thường với số tiền đủ gần (không nhỏ hơn hẳn khoản đang xét).</summary>
    private static bool ConfirmedNormalBefore(ForecastTransaction candidate, IReadOnlyList<PriorNormal> priorNormals, AnomalyRules rules)
        => priorNormals.Any(p => p.CategoryId == candidate.CategoryId
            && p.CounterpartyKey == candidate.CounterpartyKey
            && candidate.AmountCents * 100 <= p.AmountCents * rules.NormalTolerancePercent);

    private static bool SamePayee(ForecastTransaction a, ForecastTransaction b)
        => a.CounterpartyKey is not null || b.CounterpartyKey is not null
            ? a.CounterpartyKey == b.CounterpartyKey
            : a.CategoryId == b.CategoryId;

    /// <summary>Thứ tự toàn phần: theo thời điểm, cùng thời điểm thì theo id — để trong một cặp trùng chỉ một khoản bị đánh dấu.</summary>
    private static bool IsEarlier(ForecastTransaction a, ForecastTransaction b)
        => a.TransactedAt < b.TransactedAt || (a.TransactedAt == b.TransactedAt && a.Id.CompareTo(b.Id) < 0);

    private static List<double> Amounts(IEnumerable<ForecastTransaction> transactions)
        => transactions.Select(t => (double)t.AmountCents).ToList();
}
