using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Forecasting;

public sealed record ForecastAccountDto(Guid Id, string Name, bool IncludeInForecast);

/// <param name="ReserveCents">Dự phòng user tự đặt; null = dùng mặc định hệ thống.</param>
/// <param name="EffectiveReserveCents">Dự phòng đang được trừ khi tính số tiền có thể chi.</param>
/// <param name="LowBalanceThresholdCents">Ngưỡng cảnh báo số dư thấp; null = tắt.</param>
/// <param name="QuietHoursStart">Giờ yên lặng riêng "HH:mm"; null = dùng mặc định hệ thống (<paramref name="DefaultQuietHoursStart"/>).</param>
public sealed record ForecastSettingsDto(
    long? ReserveCents,
    int DefaultReservePercentOfIncome,
    long EffectiveReserveCents,
    long? LowBalanceThresholdCents,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string DefaultQuietHoursStart,
    string DefaultQuietHoursEnd,
    bool ForecastAlertsEnabled,
    IReadOnlyList<ForecastAccountDto> Accounts);

/// <summary>Thay TOÀN BỘ cài đặt dự báo. Giờ yên lặng: cả hai trống = mặc định hệ thống, cả hai "HH:mm" = giờ riêng.</summary>
public sealed record UpdateForecastSettingsCommand(
    Guid UserId,
    long? ReserveCents,
    long? LowBalanceThresholdCents,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    bool ForecastAlertsEnabled);

public sealed record TransactionForecastExclusionDto(Guid TransactionId, bool ExcludeFromForecast, string? Reason);

public interface IForecastSettingsService
{
    Task<ForecastSettingsDto> GetAsync(Guid userId, CancellationToken ct = default);
    Task<ForecastSettingsDto> SetAsync(UpdateForecastSettingsCommand command, CancellationToken ct = default);
    Task<ForecastAccountDto> SetAccountInclusionAsync(Guid userId, Guid accountId, bool included, CancellationToken ct = default);

    /// <summary>Loại (hoặc đưa lại) một giao dịch khỏi dữ liệu dự báo. Số dư không đổi: giao dịch vẫn có thật.</summary>
    Task<TransactionForecastExclusionDto> SetTransactionExclusionAsync(
        Guid userId, Guid transactionId, bool excluded, ForecastExclusionReason? reason, CancellationToken ct = default);
}

public class ForecastSettingsService : IForecastSettingsService
{
    private const long MaxAmountCents = 100_000_000_000;

    private readonly IUserRepository _users;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;

    public ForecastSettingsService(
        IUserRepository users,
        IFinancialAccountRepository accounts,
        ITransactionRepository transactions,
        ISystemConfigProvider config,
        ICacheService cache)
    {
        _users = users;
        _accounts = accounts;
        _transactions = transactions;
        _config = config;
        _cache = cache;
    }

    public async Task<ForecastSettingsDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var accounts = (await _accounts.GetListByUserAsync(userId, ct))
            .Select(a => new ForecastAccountDto(a.Id, a.AccountName, a.IncludeInForecast))
            .ToList();
        return new ForecastSettingsDto(
            user.ForecastReserveCents,
            settings.ReservePercentOfIncome,
            ForecastService.Reserve(user, settings),
            user.LowBalanceThresholdCents,
            user.QuietHoursStartMinute is { } start ? QuietHours.Format(start) : null,
            user.QuietHoursEndMinute is { } end ? QuietHours.Format(end) : null,
            QuietHours.Format(settings.QuietHoursStartMinute),
            QuietHours.Format(settings.QuietHoursEndMinute),
            user.NotificationPrefs.ForecastAlertsEnabled,
            accounts);
    }

    public async Task<ForecastSettingsDto> SetAsync(UpdateForecastSettingsCommand command, CancellationToken ct = default)
    {
        var failures = new List<ValidationFailure>();
        if (command.ReserveCents is < 0 or > MaxAmountCents)
        {
            failures.Add(new ValidationFailure(nameof(command.ReserveCents), "Khoản dự phòng phải từ 0 trở lên."));
        }

        if (command.LowBalanceThresholdCents is < 0 or > MaxAmountCents)
        {
            failures.Add(new ValidationFailure(nameof(command.LowBalanceThresholdCents), "Ngưỡng số dư thấp phải từ 0 trở lên."));
        }

        var (start, end) = (QuietHours.Parse(command.QuietHoursStart), QuietHours.Parse(command.QuietHoursEnd));
        var bothEmpty = string.IsNullOrWhiteSpace(command.QuietHoursStart) && string.IsNullOrWhiteSpace(command.QuietHoursEnd);
        if (!bothEmpty && (start is null || end is null))
        {
            failures.Add(new ValidationFailure(nameof(command.QuietHoursStart), "Giờ yên lặng cần cả giờ bắt đầu và kết thúc dạng HH:mm, hoặc để trống cả hai."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var user = await _users.GetByIdAsync(command.UserId, ct) ?? throw new NotFoundException("User", command.UserId);
        user.ForecastReserveCents = command.ReserveCents;
        user.LowBalanceThresholdCents = command.LowBalanceThresholdCents;
        user.QuietHoursStartMinute = bothEmpty ? null : (short)start!.Value;
        user.QuietHoursEndMinute = bothEmpty ? null : (short)end!.Value;
        user.NotificationPrefs.ForecastAlertsEnabled = command.ForecastAlertsEnabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);
        await _cache.RemoveAsync(CacheKeys.UserProfile(command.UserId), ct);
        return await GetAsync(command.UserId, ct);
    }

    public async Task<ForecastAccountDto> SetAccountInclusionAsync(Guid userId, Guid accountId, bool included, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
        if (account.IncludeInForecast != included)
        {
            account.IncludeInForecast = included;
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await _accounts.UpdateAsync(account, ct);
            await InvalidateAsync(userId, ct);
        }

        return new ForecastAccountDto(account.Id, account.AccountName, account.IncludeInForecast);
    }

    public async Task<TransactionForecastExclusionDto> SetTransactionExclusionAsync(
        Guid userId, Guid transactionId, bool excluded, ForecastExclusionReason? reason, CancellationToken ct = default)
    {
        var transaction = await _transactions.GetByIdAsync(transactionId, userId, ct) ?? throw new NotFoundException("Transaction", transactionId);
        var newReason = excluded ? reason ?? ForecastExclusionReason.UserMarked : (ForecastExclusionReason?)null;
        if (transaction.ExcludeFromForecast != excluded || transaction.ForecastExclusionReason != newReason)
        {
            transaction.ExcludeFromForecast = excluded;
            transaction.ForecastExclusionReason = newReason;
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            await _transactions.UpdateAsync(transaction, ct);
            await InvalidateAsync(userId, ct);
        }

        return new TransactionForecastExclusionDto(transaction.Id, transaction.ExcludeFromForecast, ReasonText(transaction.ForecastExclusionReason));
    }

    private static string? ReasonText(ForecastExclusionReason? reason) => reason switch
    {
        ForecastExclusionReason.OneOff => "one_off",
        ForecastExclusionReason.UserMarked => "user_marked",
        _ => null,
    };

    private Task InvalidateAsync(Guid userId, CancellationToken ct)
        => _cache.RemoveAsync(CacheKeys.ForecastBaseline(userId, VietnamTime.Today()), ct);
}
