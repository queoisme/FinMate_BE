using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>
/// Nguồn dữ liệu thật. Dùng <see cref="IReportRepository.GetDailySpendAsync"/>, vốn chỉ cộng giao dịch đã xác nhận loại Debit/Credit và bỏ khoản cho vay/mượn,
/// nên chuyển khoản nội bộ không bao giờ lọt vào thu nhập hay chi tiêu (NFR-DATA-07). Tháng hiện tại bị loại vì chưa trọn; ranh giới tháng theo giờ Việt Nam.
/// </summary>
public sealed class RepositoryFinancialDataSource(
    IReportRepository reports,
    IFinancialAccountRepository accounts,
    IUserRepository users) : IFinancialDataSource
{

    public async Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default)
    {
        var (year, month) = VietnamTime.YearMonthOf(now);
        var currentMonthFirst = new DateOnly(year, month, 1);
        var from = currentMonthFirst.AddMonths(-months);
        var to = currentMonthFirst.AddDays(-1);

        var points = await reports.GetDailySpendAsync(userId, from, to, ct);
        var byMonth = points
            .GroupBy(p => new DateOnly(p.Date.Year, p.Date.Month, 1))
            .ToDictionary(g => g.Key, g => (Income: g.Sum(p => p.IncomeCents), Spend: g.Sum(p => p.SpentCents)));

        var income = new List<long>();
        var spend = new List<long>();
        for (var i = 0; i < months; i++)
        {
            var key = from.AddMonths(i);
            var seen = byMonth.TryGetValue(key, out var totals);
            if (income.Count == 0 && !seen)
            {
                continue; // tháng trước khi người dùng bắt đầu ghi chép: không phải "thu nhập bằng 0"
            }

            income.Add(seen ? totals.Income : 0);
            spend.Add(seen ? totals.Spend : 0);
        }

        return new MonthlySeries(income, spend);
    }

    public async Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await reports.GetCategorySpendAsync(userId, from, to, ct);
        return rows
            .Select(r => r.CategoryId is null
                ? new CategorySpendVnd("uncategorized", "Chưa phân loại", r.SpentCents)
                : new CategorySpendVnd(r.CategorySlug ?? "category", r.CategoryName ?? "Danh mục", r.SpentCents))
            .GroupBy(r => r.Slug)
            .Select(g => new CategorySpendVnd(g.Key, g.First().Name, g.Sum(r => r.SpentVnd)))
            .OrderByDescending(r => r.SpentVnd)
            .ToList();
    }

    public async Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default)
        => (await accounts.GetListByUserAsync(userId, ct)).Where(a => a.DeletedAt is null).Sum(a => a.BalanceCents);

    public async Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user?.IncomeProfileOverride switch
        {
            "stable" => IncomeSegment.Stable,
            "variable" => IncomeSegment.Variable,
            _ => null,
        };
    }
}
