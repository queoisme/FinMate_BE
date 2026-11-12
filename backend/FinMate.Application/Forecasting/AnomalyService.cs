using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Forecasting;

public sealed record AnomalyDto(
    Guid Id,
    Guid TransactionId,
    string Kind,
    string Severity,
    long AmountCents,
    long TypicalCents,
    int RatioPercent,
    Guid? CategoryId,
    string? MerchantName,
    DateTimeOffset? TransactedAt,
    string? Resolution,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);

/// <summary>Một đánh dấu vừa tạo, kèm thông tin giao dịch để viết nội dung thông báo.</summary>
public sealed record DetectedAnomaly(TransactionAnomaly Anomaly, string? MerchantName, DateOnly Date);

/// <param name="Guidance">Chỉ có khi phản hồi là "giao dịch lạ": hướng dẫn liên hệ ngân hàng.</param>
public sealed record AnomalyFeedbackResultDto(AnomalyDto Anomaly, string? Guidance);

public sealed record AnomalyListResult(IReadOnlyList<AnomalyDto> Items, string? NextCursor);

public interface IAnomalyService
{
    /// <summary>
    /// Xét các khoản chi gần đây chưa từng được đánh dấu, rồi trả MỌI đánh dấu còn mở trong cửa sổ quét — cả mới lẫn cũ. Trả cả cũ để bên gọi gửi lại
    /// được thông báo nếu lượt trước hỏng giữa chừng (sau khi đánh dấu, trước khi ghi cảnh báo); hộp thư cảnh báo chống trùng nên không báo hai lần.
    /// </summary>
    Task<IReadOnlyList<DetectedAnomaly>> ScanAsync(Guid userId, CancellationToken ct = default);

    Task<AnomalyListResult> ListAsync(Guid userId, bool openOnly, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Phản hồi: <c>normal</c> | <c>one_off</c> | <c>wrong_entry</c> | <c>suspicious</c>. Đổi ý được (phản hồi sau thay phản hồi trước).</summary>
    Task<AnomalyFeedbackResultDto> SetFeedbackAsync(Guid userId, Guid anomalyId, string? resolution, CancellationToken ct = default);
}

public class AnomalyService : IAnomalyService
{
    public const string SuspiciousGuidance =
        "Nếu bạn không thực hiện giao dịch này: gọi ngay tổng đài ngân hàng hoặc ví điện tử (số in sau thẻ hoặc trong ứng dụng ngân hàng) "
        + "để khóa thẻ/tài khoản, đổi mật khẩu, và không cung cấp mã OTP cho bất kỳ ai.";

    private const int MaxListLimit = 100;

    private static readonly Dictionary<string, AnomalyResolution> ResolutionByText = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"] = AnomalyResolution.Normal,
        ["one_off"] = AnomalyResolution.OneOff,
        ["wrong_entry"] = AnomalyResolution.WrongEntry,
        ["suspicious"] = AnomalyResolution.Suspicious,
    };

    private readonly IAnomalyRepository _anomalies;
    private readonly IForecastDataRepository _data;
    private readonly IRecurringItemRepository _items;
    private readonly IForecastSettingsService _forecastSettings;
    private readonly ISystemConfigProvider _config;

    public AnomalyService(
        IAnomalyRepository anomalies,
        IForecastDataRepository data,
        IRecurringItemRepository items,
        IForecastSettingsService forecastSettings,
        ISystemConfigProvider config)
    {
        _anomalies = anomalies;
        _data = data;
        _items = items;
        _forecastSettings = forecastSettings;
        _config = config;
    }

    public async Task<IReadOnlyList<DetectedAnomaly>> ScanAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var today = VietnamTime.Today();
        var history = await _data.GetTransactionsAsync(userId, today.AddDays(-settings.AnomalyLookbackDays), today, ct);
        var scanFrom = today.AddDays(1 - settings.AnomalyScanDays);
        var recent = history.Where(t => t.Date >= scanFrom && t.Type == TransactionType.Debit && !t.ExcludedFromForecast).ToList();
        if (recent.Count == 0)
        {
            return [];
        }

        var existing = await _anomalies.GetForTransactionsAsync(userId, recent.Select(t => t.Id).ToList(), ct);
        var byId = recent.ToDictionary(t => t.Id);
        var open = existing
            .Where(a => a.Resolution is null)
            .Select(a => new DetectedAnomaly(a, byId[a.TransactionId].MerchantName, byId[a.TransactionId].Date))
            .ToList();
        var flagged = existing.Select(a => a.TransactionId).ToHashSet();
        var candidates = recent.Where(t => !flagged.Contains(t.Id)).OrderBy(t => t.TransactedAt).ToList();
        if (candidates.Count == 0)
        {
            return open;
        }

        var active = (await _items.ListAsync(userId, RecurringStatus.Active, ct)).Select(i => i.ToView()).ToList();
        var normals = await _anomalies.GetPriorNormalsAsync(userId, ct);
        var (forecastRules, rules) = (ForecastRules.From(settings), AnomalyRules.From(settings));
        foreach (var candidate in candidates)
        {
            if (AnomalyDetector.Evaluate(candidate, history, active, normals, forecastRules, rules) is not { } finding)
            {
                continue;
            }

            // Thua cuộc đua với một lượt quét song song thì đánh dấu đã có; lượt kia (hoặc lượt sau) sẽ trả nó ra.
            var anomaly = ToEntity(userId, candidate, finding);
            if (await _anomalies.TryAddAsync(anomaly, ct))
            {
                open.Add(new DetectedAnomaly(anomaly, candidate.MerchantName, candidate.Date));
            }
        }

        return open;
    }

    public async Task<AnomalyListResult> ListAsync(Guid userId, bool openOnly, string? cursor, int limit, CancellationToken ct = default)
    {
        var page = await _anomalies.ListAsync(userId, openOnly, cursor, ForecastPaging.Clamp(limit, MaxListLimit), ct);
        return new AnomalyListResult(page.Items.Select(ToDto).ToList(), page.NextCursor);
    }

    public async Task<AnomalyFeedbackResultDto> SetFeedbackAsync(Guid userId, Guid anomalyId, string? resolution, CancellationToken ct = default)
    {
        if (resolution is null || !ResolutionByText.TryGetValue(resolution, out var value))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(resolution), "Phản hồi phải là một trong: normal, one_off, wrong_entry, suspicious.")]);
        }

        var anomaly = await _anomalies.GetAsync(anomalyId, userId, ct) ?? throw new NotFoundException("TransactionAnomaly", anomalyId);
        var previous = anomaly.Resolution;
        if (value == AnomalyResolution.OneOff && previous != AnomalyResolution.OneOff)
        {
            await _forecastSettings.SetTransactionExclusionAsync(userId, anomaly.TransactionId, true, ForecastExclusionReason.OneOff, ct);
        }
        else if (value != AnomalyResolution.OneOff && previous == AnomalyResolution.OneOff)
        {
            // Đổi ý khỏi "chi một lần": trả giao dịch về dữ liệu dự báo như trước khi phản hồi.
            await _forecastSettings.SetTransactionExclusionAsync(userId, anomaly.TransactionId, false, null, ct);
        }

        if (previous != value)
        {
            anomaly.Resolution = value;
            anomaly.ResolvedAt = DateTimeOffset.UtcNow;
            await _anomalies.UpdateAsync(anomaly, ct);
        }

        return new AnomalyFeedbackResultDto(ToDto(anomaly), value == AnomalyResolution.Suspicious ? SuspiciousGuidance : null);
    }

    private static TransactionAnomaly ToEntity(Guid userId, ForecastTransaction candidate, AnomalyFinding finding) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TransactionId = candidate.Id,
        CategoryId = candidate.CategoryId,
        CounterpartyKey = candidate.CounterpartyKey,
        Kind = finding.Kind,
        Severity = finding.Severity,
        AmountCents = candidate.AmountCents,
        TypicalCents = finding.TypicalCents,
        RatioPercent = finding.RatioPercent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static string KindText(AnomalyKind kind) => kind switch
    {
        AnomalyKind.CategorySpike => "category_spike",
        AnomalyKind.MerchantSpike => "merchant_spike",
        AnomalyKind.Duplicate => "duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string? ResolutionText(AnomalyResolution? resolution)
        => resolution is { } r ? ResolutionByText.First(p => p.Value == r).Key : null;

    private static AnomalyDto ToDto(TransactionAnomaly a) => new(
        a.Id,
        a.TransactionId,
        KindText(a.Kind),
        a.Severity.ToString().ToLowerInvariant(),
        a.AmountCents,
        a.TypicalCents,
        a.RatioPercent,
        a.CategoryId,
        a.Transaction?.MerchantName,
        a.Transaction?.TransactedAt,
        ResolutionText(a.Resolution),
        a.ResolvedAt,
        a.CreatedAt);
}
