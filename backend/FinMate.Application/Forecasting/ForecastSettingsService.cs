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
public sealed record ForecastSettingsDto(
    long? ReserveCents,
    int DefaultReservePercentOfIncome,
    long EffectiveReserveCents,
    IReadOnlyList<ForecastAccountDto> Accounts);

public sealed record TransactionForecastExclusionDto(Guid TransactionId, bool ExcludeFromForecast, string? Reason);

public interface IForecastSettingsService
{
    Task<ForecastSettingsDto> GetAsync(Guid userId, CancellationToken ct = default);
    Task<ForecastSettingsDto> SetReserveAsync(Guid userId, long? reserveCents, CancellationToken ct = default);
    Task<ForecastAccountDto> SetAccountInclusionAsync(Guid userId, Guid accountId, bool included, CancellationToken ct = default);

    /// <summary>Loại (hoặc đưa lại) một giao dịch khỏi dữ liệu dự báo. Số dư không đổi: giao dịch vẫn có thật.</summary>
    Task<TransactionForecastExclusionDto> SetTransactionExclusionAsync(
        Guid userId, Guid transactionId, bool excluded, ForecastExclusionReason? reason, CancellationToken ct = default);
}

public class ForecastSettingsService : IForecastSettingsService
{
    private const long MaxReserveCents = 100_000_000_000;

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
        return new ForecastSettingsDto(user.ForecastReserveCents, settings.ReservePercentOfIncome, ForecastService.Reserve(user, settings), accounts);
    }

    public async Task<ForecastSettingsDto> SetReserveAsync(Guid userId, long? reserveCents, CancellationToken ct = default)
    {
        if (reserveCents is < 0 or > MaxReserveCents)
        {
            throw new ValidationException([new ValidationFailure(nameof(reserveCents), "Khoản dự phòng phải từ 0 trở lên.")]);
        }

        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        user.ForecastReserveCents = reserveCents;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);
        await _cache.RemoveAsync(CacheKeys.UserProfile(userId), ct);
        return await GetAsync(userId, ct);
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
