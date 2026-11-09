using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Subscriptions;

public sealed record FeatureReservation(Guid UserId, string FeatureKey, DateOnly Month);

public sealed record FeatureUsageDto(string Key, string Name, bool Enabled, int? MonthlyLimit, int Used);

public sealed record SubscriptionPeriodDto(string PlanCode, string PlanName, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record MySubscriptionDto(
    bool Enabled,
    string PlanCode,
    string PlanName,
    bool IsFree,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset NextPurchaseStartsAt,
    IReadOnlyList<SubscriptionPeriodDto> Upcoming,
    IReadOnlyList<FeatureUsageDto> Features);

public interface ISubscriptionEntitlementService
{
    /// <summary>
    /// Giữ một lượt dùng. Null nghĩa là không đếm: cờ đang tắt, hoặc đây là lần gửi lại của một giao dịch đã lưu. Không được dùng thì ném lỗi 422.
    /// </summary>
    /// <param name="clientRequestId">Mã chống trùng của client. Gửi lại khi mất phản hồi không được ăn thêm lượt hay bị từ chối vì hết lượt, nếu không client không bao giờ biết giao dịch đã lưu.</param>
    Task<FeatureReservation?> ReserveAsync(Guid userId, string featureKey, Guid? clientRequestId = null, CancellationToken ct = default);

    /// <summary>Trả lại lượt khi chức năng lỗi, để lỗi của hệ thống không ăn lượt của khách.</summary>
    Task ReleaseAsync(FeatureReservation reservation, CancellationToken ct = default);

    Task<MySubscriptionDto> GetMineAsync(Guid userId, CancellationToken ct = default);
}

public sealed class SubscriptionEntitlementService : ISubscriptionEntitlementService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISubscriptionRepository _repository;
    private static readonly TimeSpan ReplayMarkerTtl = TimeSpan.FromHours(24);

    private readonly ISystemConfigProvider _config;
    private readonly ITransactionRepository _transactions;
    private readonly ICacheService _cache;

    public SubscriptionEntitlementService(ISubscriptionRepository repository, ISystemConfigProvider config, ITransactionRepository transactions, ICacheService cache)
    {
        _transactions = transactions;
        _cache = cache;
        _repository = repository;
        _config = config;
    }

    public async Task<FeatureReservation?> ReserveAsync(Guid userId, string featureKey, Guid? clientRequestId = null, CancellationToken ct = default)
    {
        if (!(await _config.GetAsync(ct)).Document.Features.Subscriptions
            || (clientRequestId is { } requestId && await IsFirstReplayAsync(userId, requestId, ct)))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var periods = await _repository.ListCurrentAndUpcomingPeriodsAsync(userId, now, ct);
        var freePlan = await _repository.GetDefaultFreePlanAsync(ct);
        var grant = Resolve(CurrentOf(periods, now), freePlan).GetValueOrDefault(featureKey);
        if (grant is null || !grant.Enabled)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.FeatureLocked, "Gói hiện tại của bạn chưa có chức năng này.");
        }

        var month = MonthOf(now);
        var limit = grant.MonthlyLimit ?? int.MaxValue;
        if (limit <= 0 || !await _repository.TryConsumeAsync(userId, featureKey, month, limit, ct))
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.LimitReached, "Bạn đã dùng hết lượt của chức năng này trong tháng.");
        }

        return new FeatureReservation(userId, featureKey, month);
    }

    /// <summary>Mỗi mã chỉ được miễn một lần. Handler vẫn gọi AI trước khi nhận ra gửi lại, nên miễn không giới hạn thì dùng lại một mã là có AI không tốn lượt.</summary>
    private async Task<bool> IsFirstReplayAsync(Guid userId, Guid clientRequestId, CancellationToken ct)
    {
        if (await _transactions.GetByClientRequestIdAsync(userId, clientRequestId, ct) is null)
        {
            return false;
        }

        var marker = $"subscription-replay:{userId}:{clientRequestId}";
        if (await _cache.GetAsync<string>(marker, ct) is not null)
        {
            return false;
        }

        await _cache.SetAsync(marker, "1", ReplayMarkerTtl, ct);
        return true;
    }

    public Task ReleaseAsync(FeatureReservation reservation, CancellationToken ct = default)
        => _repository.ReleaseAsync(reservation.UserId, reservation.FeatureKey, reservation.Month, ct);

    public async Task<MySubscriptionDto> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var enabled = (await _config.GetAsync(ct)).Document.Features.Subscriptions;
        var periods = await _repository.ListCurrentAndUpcomingPeriodsAsync(userId, now, ct);
        var freePlan = await _repository.GetDefaultFreePlanAsync(ct);
        var current = CurrentOf(periods, now);
        var grants = Resolve(current, freePlan);
        var usage = (await _repository.ListUsageAsync(userId, MonthOf(now), ct)).ToDictionary(u => u.FeatureKey, u => u.Used);

        var features = SubscriptionFeatures.All
            .Select(f =>
            {
                var grant = grants.GetValueOrDefault(f.Key);
                return new FeatureUsageDto(f.Key, f.Name, grant?.Enabled ?? false, grant?.MonthlyLimit, usage.GetValueOrDefault(f.Key));
            })
            .ToList();

        return new MySubscriptionDto(
            enabled,
            current?.PlanCode ?? freePlan?.Code ?? string.Empty,
            current?.PlanName ?? freePlan?.Name ?? string.Empty,
            current is null,
            current?.EndsAt,
            SubscriptionPeriods.NextStart(now, periods.Count == 0 ? null : periods.Max(p => p.EndsAt)),
            periods.Where(p => p != current).Select(p => new SubscriptionPeriodDto(p.PlanCode, p.PlanName, p.StartsAt, p.EndsAt)).ToList(),
            features);
    }

    private static UserSubscriptionPeriod? CurrentOf(List<UserSubscriptionPeriod> periods, DateTimeOffset now)
        => periods.FirstOrDefault(p => p.StartsAt <= now && now < p.EndsAt);

    /// <summary>Tháng tính theo giờ Việt Nam.</summary>
    private static DateOnly MonthOf(DateTimeOffset now)
    {
        var (year, month) = VietnamTime.YearMonthOf(now);
        return new DateOnly(year, month, 1);
    }

    /// <summary>Kỳ đã mua giữ đúng bản chụp. Chức năng ra đời sau lúc mua thì theo gói miễn phí, vì bản chụp không nói gì về nó.</summary>
    private static Dictionary<string, FeatureGrant> Resolve(UserSubscriptionPeriod? current, SubscriptionPlan? freePlan)
    {
        var grants = (freePlan?.Features ?? []).ToDictionary(f => f.FeatureKey, f => new FeatureGrant(f.FeatureKey, f.Enabled, f.MonthlyLimit));
        if (current is not null)
        {
            foreach (var grant in JsonSerializer.Deserialize<List<FeatureGrant>>(current.FeaturesJson, Json) ?? [])
            {
                grants[grant.Key] = grant;
            }
        }

        return grants;
    }
}
