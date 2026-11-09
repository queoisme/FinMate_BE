using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

/// <summary>Chỉ để điền sẵn: không có gì được đăng cho tới khi người dùng xác nhận, vì tên từ thông báo chuyển khoản có thể là tên người thật.</summary>
public sealed record CommunitySharePromptDto(bool ShouldPrompt, string? SuggestedPlaceName, long? SuggestedPriceCents, IReadOnlyList<CommunityPlaceDto> ExistingPlaces);

public interface ICommunitySharePromptService
{
    Task<CommunitySharePromptDto> GetAsync(Guid userId, Guid transactionId, CancellationToken ct = default);
}

/// <summary>
/// App gọi sau khi lưu hoặc xác nhận giao dịch, nên việc này không nằm trong đường ghi tiền và lỗi ở đây không ảnh hưởng gì tới giao dịch.
/// Lời gọi có trả lời "có" sẽ tính là đã hỏi, nên app chỉ gọi một lần cho mỗi giao dịch.
/// </summary>
public sealed class CommunitySharePromptService : ICommunitySharePromptService
{
    private const string FoodSlug = "food";
    private const string Marker = "1";
    private const int MaxExistingPlaces = 5;

    private static readonly CommunitySharePromptDto No = new(false, null, null, []);

    private readonly ITransactionRepository _transactions;
    private readonly IUserRepository _users;
    private readonly ICommunityRepository _community;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;

    public CommunitySharePromptService(ITransactionRepository transactions, IUserRepository users, ICommunityRepository community, ISystemConfigProvider config, ICacheService cache)
    {
        _transactions = transactions;
        _users = users;
        _community = community;
        _config = config;
        _cache = cache;
    }

    public async Task<CommunitySharePromptDto> GetAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var transaction = await _transactions.GetByIdAsync(transactionId, userId, ct) ?? throw new NotFoundException("Transaction", transactionId);
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        var settings = (await _config.GetAsync(ct)).Document.Community;
        if (!user.CommunityPromptsEnabled || !IsEligible(transaction, settings))
        {
            return No;
        }

        var key = CounterpartyKey.From(transaction.MerchantName);
        if (key is not null && await _community.HasOwnPostForPlaceKeyAsync(userId, key, ct))
        {
            return No;
        }

        if (!await TryClaimAsync(userId, key, settings, ct))
        {
            return No;
        }

        return new CommunitySharePromptDto(true, transaction.MerchantName, transaction.AmountCents, await ExistingPlacesAsync(user, key, ct));
    }

    private static bool IsEligible(Transaction transaction, CommunitySettings settings)
        => transaction.TransactionType == TransactionType.Debit
            && transaction.Status == TransactionStatus.Confirmed
            && transaction.Category?.Slug != Category.LoanSlug
            && transaction.AmountCents <= settings.PromptMaxAmountCents
            && IsFood(transaction, settings);

    private static bool IsFood(Transaction transaction, CommunitySettings settings)
        => transaction.Category?.Slug == FoodSlug
            || MarketKeywords.MatchesTransaction($"{transaction.MerchantName} {transaction.Description} {transaction.Category?.Name}", settings.FoodKeywords, settings.FoodExcludes);

    /// <summary>Lời nhắc chỉ là phần thêm, nên Redis lỗi thì coi như không hỏi. Mất dữ liệu Redis thì tệ nhất là hỏi thêm một lần.</summary>
    private async Task<bool> TryClaimAsync(Guid userId, string? placeKey, CommunitySettings settings, CancellationToken ct)
    {
        var dayKey = CacheKeys.CommunityPromptDay(userId, VietnamTime.Today());
        var placeCacheKey = placeKey is null ? null : CacheKeys.CommunityPromptPlace(userId, Hash(placeKey));
        try
        {
            if (await _cache.GetAsync<string>(dayKey, ct) is not null
                || (placeCacheKey is not null && await _cache.GetAsync<string>(placeCacheKey, ct) is not null))
            {
                return false;
            }

            await _cache.SetAsync(dayKey, Marker, CacheKeys.CommunityPromptDayTtl, ct);
            if (placeCacheKey is not null)
            {
                await _cache.SetAsync(placeCacheKey, Marker, TimeSpan.FromDays(settings.PromptPerPlaceCooldownDays), ct);
            }

            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<CommunityPlaceDto>> ExistingPlacesAsync(User user, string? key, CancellationToken ct)
    {
        if (key is null || user.CommunityProvinceCode is null)
        {
            return [];
        }

        var places = await _community.FindPlacesByKeyAsync(key, user.CommunityProvinceCode, MaxExistingPlaces, ct);
        return places.Select(p => new CommunityPlaceDto(p.Id, p.Name, p.ProvinceCode, p.AreaCode, p.AddressText)).ToList();
    }

    /// <summary>Băm tên quán: khóa Redis không nên cho thấy người này đã chi ở đâu.</summary>
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
