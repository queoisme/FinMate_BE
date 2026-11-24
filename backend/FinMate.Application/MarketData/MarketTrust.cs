using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

/// <summary>Hạng của một nguồn theo danh sách <see cref="MarketDataSettings.TrustedDomains"/>. Không có tên trong danh sách thì không được dùng.</summary>
public static class MarketTrust
{
    public const string Untrusted = "untrusted";

    /// <summary><c>official</c>, <c>licensed</c> hoặc <see cref="Untrusted"/>.</summary>
    public static string TierOf(string url, MarketDataSettings settings) => Find(MarketDomains.Of(url), settings)?.Tier ?? Untrusted;

    public static bool IsTrusted(string url, MarketDataSettings settings) => Find(MarketDomains.Of(url), settings) is not null;

    /// <summary>Đơn vị chủ quản, dùng để đếm nguồn độc lập: hai tên miền cùng chủ quản là một nguồn. Null khi không có trong danh sách.</summary>
    public static string? OwnerOf(string domain, MarketDataSettings settings) => Find(domain, settings)?.Owner.Trim().ToLowerInvariant();

    public static MarketTrustedDomain? Find(string domain, MarketDataSettings settings)
        => settings.TrustedDomains.FirstOrDefault(d => d.Domain == domain);
}
