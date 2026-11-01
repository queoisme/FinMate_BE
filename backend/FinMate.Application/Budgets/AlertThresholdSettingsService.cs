using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Budgets;

public sealed record ThresholdPair(int T1Pct, int T2Pct);

/// <summary>Ngưỡng cảnh báo ngân sách của MỘT người dùng và nguồn của nó, đủ để app biết hiện màn nào.</summary>
/// <param name="T1Pct">Ngưỡng nhắc nhẹ đang có hiệu lực.</param>
/// <param name="T2Pct">Ngưỡng cảnh báo mạnh đang có hiệu lực.</param>
/// <param name="Source"><c>method</c> (theo cách quản lý đang dùng, chỉnh ở màn cách quản lý), <c>personal</c> (ngưỡng cá nhân) hoặc <c>system</c> (mặc định hệ thống).</param>
/// <param name="Personal">Ngưỡng cá nhân đã lưu (null = chưa đặt); có thể đang bị cách quản lý che.</param>
/// <param name="SystemDefault">Mặc định hệ thống do admin đặt, để app hiện "mặc định là ...".</param>
public sealed record AlertThresholdSettingsDto(
    int T1Pct,
    int T2Pct,
    string Source,
    string? MethodCode,
    string? MethodName,
    ThresholdPair? Personal,
    ThresholdPair SystemDefault);

public interface IAlertThresholdSettingsService
{
    Task<AlertThresholdSettingsDto> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Lưu ngưỡng cá nhân. Hợp lệ khi 1 ≤ T1 &lt; T2 ≤ 99. Lưu được cả khi đang dùng cách quản lý có ngưỡng riêng: giá trị này chỉ có hiệu lực khi
    /// không còn cách nào định nghĩa T1/T2 (xem <see cref="IAlertThresholdProvider.ResolveAsync"/>).
    /// </summary>
    Task<AlertThresholdSettingsDto> SetPersonalAsync(Guid userId, int t1Pct, int t2Pct, CancellationToken ct = default);

    /// <summary>Xóa ngưỡng cá nhân: quay về mặc định hệ thống.</summary>
    Task<AlertThresholdSettingsDto> ClearPersonalAsync(Guid userId, CancellationToken ct = default);
}

public class AlertThresholdSettingsService : IAlertThresholdSettingsService
{
    private readonly IAlertThresholdProvider _thresholds;
    private readonly IUserRepository _users;

    public AlertThresholdSettingsService(IAlertThresholdProvider thresholds, IUserRepository users)
    {
        _thresholds = thresholds;
        _users = users;
    }

    public async Task<AlertThresholdSettingsDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        var resolution = await _thresholds.ResolveAsync(userId, ct);
        var system = await _thresholds.GetSystemDefaultAsync(ct);
        var prefs = user.NotificationPrefs;
        var personal = prefs.AlertT1Pct is { } t1 && prefs.AlertT2Pct is { } t2 ? new ThresholdPair(t1, t2) : null;

        return new AlertThresholdSettingsDto(
            resolution.Thresholds.T1Pct,
            resolution.Thresholds.T2Pct,
            resolution.Source.ToString().ToLowerInvariant(),
            resolution.MethodCode,
            resolution.MethodName,
            personal,
            new ThresholdPair(system.T1Pct, system.T2Pct));
    }

    public async Task<AlertThresholdSettingsDto> SetPersonalAsync(Guid userId, int t1Pct, int t2Pct, CancellationToken ct = default)
    {
        if (!AlertThresholds.IsValid(new AlertThresholds(t1Pct, t2Pct)))
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.AlertThresholdInvalid,
                "Ngưỡng nhắc nhẹ (T1) phải nhỏ hơn ngưỡng cảnh báo mạnh (T2); T1 từ 1% và T2 tối đa 99% (mức vượt hạn mức luôn là 100%).");
        }

        await SaveAsync(userId, t1Pct, t2Pct, ct);
        return await GetAsync(userId, ct);
    }

    public async Task<AlertThresholdSettingsDto> ClearPersonalAsync(Guid userId, CancellationToken ct = default)
    {
        await SaveAsync(userId, null, null, ct);
        return await GetAsync(userId, ct);
    }

    private async Task SaveAsync(Guid userId, int? t1Pct, int? t2Pct, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        user.NotificationPrefs.AlertT1Pct = t1Pct;
        user.NotificationPrefs.AlertT2Pct = t2Pct;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);
    }
}
