using FinMate.Application.Admin.SystemConfig;
using FinMate.Domain.Entities;

namespace FinMate.Application.MarketData;

/// <summary>Thói quen chi của một người cho một loại chi phí, tính lại mỗi lần từ giao dịch họ đã có. Không lưu thêm gì và không gửi đi đâu.</summary>
/// <param name="TimesPerMonth">Số lần chi trung bình mỗi tháng, đã làm tròn xuống.</param>
/// <param name="MainCategoryId">Danh mục người đó ghi khoản này nhiều tiền nhất. Có thể là danh mục họ tự tạo.</param>
public sealed record MarketSpendProfile(int Count, long TotalCents, long MonthlyCents, int TimesPerMonth, int SharePercent, DateTimeOffset LastSpentAt, Guid? MainCategoryId)
{
    private const int DaysPerMonth = 30;

    public static bool Matches(Transaction transaction, MarketCostType type)
    {
        if (transaction.Category?.Slug is { } slug && type.CategorySlugs.Contains(slug))
        {
            return true;
        }

        return MarketKeywords.MatchesTransaction($"{transaction.MerchantName} {transaction.Description} {transaction.Category?.Name}", type.TransactionKeywords, type.TransactionExcludes);
    }

    /// <summary>Null khi người đó không có giao dịch nào của loại chi phí này.</summary>
    public static MarketSpendProfile? Build(IReadOnlyList<Transaction> debits, MarketCostType type, int lookbackDays)
    {
        var matched = debits.Where(t => Matches(t, type)).ToList();
        if (matched.Count == 0)
        {
            return null;
        }

        var total = matched.Sum(t => t.AmountCents);
        var allSpend = debits.Sum(t => t.AmountCents);
        var mainCategory = matched
            .Where(t => t.CategoryId is not null)
            .GroupBy(t => t.CategoryId)
            .OrderByDescending(group => group.Sum(t => t.AmountCents))
            .Select(group => group.Key)
            .FirstOrDefault();

        return new MarketSpendProfile(
            matched.Count,
            total,
            total * DaysPerMonth / lookbackDays,
            matched.Count * DaysPerMonth / lookbackDays,
            allSpend <= 0 ? 0 : (int)(total * 100 / allSpend),
            matched.Max(t => t.TransactedAt),
            mainCategory);
    }

    /// <summary>Có chi đủ nhiều, đủ gần đây và đủ đáng kể để lời nhắc có ích.</summary>
    public bool IsRelevant(MarketDataSettings settings, DateTimeOffset now)
        => Count >= settings.MinCategoryTransactions
            && SharePercent >= settings.MinSpendSharePercent
            && LastSpentAt >= now.AddDays(-settings.LastSpendMaxDays);
}
