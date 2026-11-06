using FinMate.Application.Common;
using FinMate.Domain.Entities;

namespace FinMate.Application.Community;

/// <summary>Một lượt chia sẻ còn hiển thị của một quán.</summary>
public sealed record CommunityPlaceShare(long? PriceCents, bool SpentHereVerified, int Votes, DateTimeOffset CreatedAt, IReadOnlyList<string> Tags);

/// <summary>Hàm thuần. Chi thật đáng tin hơn vote vì khó giả hơn; quán lâu không ai nhắc thì giảm dần nhưng không về 0.</summary>
public static class CommunityPlaceRanker
{
    private const int SpentHereWeight = 3;
    private const double HalfLifeDays = 90;
    private const double MinRecency = 0.25;

    public static double Score(IReadOnlyList<CommunityPlaceShare> shares, DateTimeOffset now)
    {
        var raw = shares.Count(s => s.SpentHereVerified) * SpentHereWeight + shares.Sum(s => s.Votes) + shares.Count;
        var ageDays = Math.Max(0, (now - shares.Max(s => s.CreatedAt)).TotalDays);
        return raw * Math.Max(MinRecency, Math.Pow(0.5, ageDays / HalfLifeDays));
    }

    /// <summary>Trung vị, số chẵn phần tử thì lấy số nhỏ hơn: không chia tiền rồi làm tròn.</summary>
    public static long? TypicalPrice(IReadOnlyList<CommunityPlaceShare> shares)
    {
        var prices = shares.Where(s => s.PriceCents is not null).Select(s => s.PriceCents!.Value).Order().ToList();
        return prices.Count == 0 ? null : prices[(prices.Count - 1) / 2];
    }

    public static CommunityPlaceSummaryDto Summarize(CommunityPlace place, IReadOnlyList<CommunityPlaceShare> shares)
        => new(
            place.Id,
            place.Name,
            place.ProvinceCode,
            place.AreaCode,
            place.AddressText,
            shares.Count,
            shares.Count(s => s.SpentHereVerified),
            shares.Sum(s => s.Votes),
            TypicalPrice(shares),
            shares.SelectMany(s => s.Tags).Distinct().Order().ToList(),
            VietnamTime.DateOf(shares.Max(s => s.CreatedAt)));
}
