using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Reports;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly FinMateDbContext _context;

    public ReportRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<DailySummary?> GetDailySummaryAsync(Guid userId, DateOnly date, CancellationToken ct = default)
        => _context.DailySummaries.FirstOrDefaultAsync(s => s.UserId == userId && s.SummaryDate == date, ct);

    public Task<List<DailySummary>> GetDailySummariesAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => _context.DailySummaries
            .Where(s => s.UserId == userId && s.SummaryDate >= from && s.SummaryDate <= to)
            .OrderBy(s => s.SummaryDate)
            .ToListAsync(ct);

    public async Task UpsertDailySummaryAsync(DailySummary summary, CancellationToken ct = default)
    {
        var existing = await GetDailySummaryAsync(summary.UserId, summary.SummaryDate, ct);

        if (existing is null)
        {
            _context.DailySummaries.Add(summary);
        }
        else
        {
            existing.TotalSpentCents = summary.TotalSpentCents;
            existing.TotalIncomeCents = summary.TotalIncomeCents;
            existing.TotalRefundedCents = summary.TotalRefundedCents;
            existing.TransactionCount = summary.TransactionCount;
            existing.TopCategoryId = summary.TopCategoryId;
            existing.TopCategorySpentCents = summary.TopCategorySpentCents;
            existing.UpdatedAt = summary.UpdatedAt;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<DailySpendPoint>> GetDailySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);

        // Gom theo ngày giờ VN phải làm trong bộ nhớ: biểu thức cắt ngày theo offset +07
        // không dịch được sang SQL đáng tin cậy qua EF/Npgsql. Chỉ kéo về 3 cột cần dùng và
        // khoảng thời gian luôn bị chặn (vài tháng của MỘT user), nên chi phí không đáng kể.
        var rows = await _context.Transactions
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && (t.Category == null || t.Category.Slug != Category.LoanSlug)  // cho vay/mượn không phải chi tiêu hay thu nhập
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .Select(t => new { t.TransactedAt, t.AmountCents, t.TransactionType, t.Relation, t.IsReversed })
            .ToListAsync(ct);

        // Hoàn tiền / đảo giao dịch (Phase 81): một quy tắc chung cho mọi báo cáo, xem NetFlow.
        return rows
            .GroupBy(t => VietnamTime.DateOf(t.TransactedAt))
            .Select(g =>
            {
                var parts = g.Select(t => NetFlow.Of(t.TransactionType, t.Relation, t.IsReversed, t.AmountCents)).ToList();
                return new DailySpendPoint(
                    g.Key,
                    parts.Sum(p => p.SpentCents),
                    parts.Sum(p => p.IncomeCents),
                    parts.Count(p => p.Counted),
                    parts.Sum(p => p.RefundedCents));
            })
            .Where(p => p.TransactionCount > 0)
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<List<CategorySpend>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);

        // Cùng định nghĩa với ngân sách (BudgetSpendScope): chi − hoàn theo danh mục (khoản hoàn mang danh mục của khoản gốc), bỏ cặp đảo và
        // cho vay/mượn. Danh mục ròng ≤ 0 (chỉ có hoàn cho món mua kỳ trước) không có dòng: cơ cấu và phần trăm không nhận số âm (D3).
        var rows = await _context.Transactions
            .Where(BudgetSpendScope.Personal(userId, null, start, end))
            .Where(BudgetSpendScope.Counts)
            .GroupBy(t => new { t.CategoryId, Name = t.Category!.Name, Slug = t.Category!.Slug })
            .Select(g => new CategorySpend(
                g.Key.CategoryId,
                g.Key.Name,
                g.Key.Slug,
                g.Sum(t => t.TransactionType == TransactionType.Debit ? t.AmountCents : -t.AmountCents)))
            .ToListAsync(ct);

        return rows.Where(r => r.SpentCents > 0).OrderByDescending(r => r.SpentCents).ToList();
    }

    public async Task<long> GetCrossPeriodRefundsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);
        return await _context.Transactions
            .Where(BudgetSpendScope.Personal(userId, null, start, end))
            .Where(BudgetSpendScope.Counts)
            .Where(t => t.Relation == TransactionRelation.Refund
                && t.RelatedTransaction != null
                && (t.RelatedTransaction.TransactedAt < start || t.RelatedTransaction.TransactedAt >= end))
            .SumAsync(t => t.AmountCents, ct);
    }

    public Task<List<Guid>> GetUserIdsWithActivityAsync(DateOnly date, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(date);

        return _context.Transactions
            .Where(t => t.Status == TransactionStatus.Confirmed && t.TransactedAt >= start && t.TransactedAt < end)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<List<Guid>> GetUserIdsWithRecentActivityAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);

        return _context.Transactions
            .Where(t => t.Status == TransactionStatus.Confirmed && t.TransactedAt >= start && t.TransactedAt < end)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<List<SpendingInsight>> GetInsightsAsync(Guid userId, bool unreadOnly, int limit, CancellationToken ct = default)
    {
        var query = _context.SpendingInsights.Include(i => i.Category).Where(i => i.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(i => !i.IsRead);
        }

        return query.OrderByDescending(i => i.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public Task<SpendingInsight?> GetInsightAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.SpendingInsights
            .Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);

    public Task<bool> InsightExistsAsync(Guid userId, InsightType type, Guid? categoryId, DateOnly periodStart, CancellationToken ct = default)
        => _context.SpendingInsights.AnyAsync(
            i => i.UserId == userId
                && i.InsightType == type
                && i.CategoryId == categoryId
                && i.PeriodStart == periodStart,
            ct);

    public async Task AddInsightAsync(SpendingInsight insight, CancellationToken ct = default)
    {
        _context.SpendingInsights.Add(insight);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateInsightAsync(SpendingInsight insight, CancellationToken ct = default)
    {
        _context.SpendingInsights.Update(insight);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<Transaction>> GetConfirmedDebitsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);

        return _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType == TransactionType.Debit
                && !t.IsReversed
                && (t.Category == null || t.Category.Slug != Category.LoanSlug)
                && t.TransactedAt >= start
                && t.TransactedAt < end
                // Khoản đã được hoàn 100% không phải chi tiêu thật (Phase 81, BR-RF-07).
                && (_context.Transactions
                    .Where(r => r.RelatedTransactionId == t.Id && r.Status == TransactionStatus.Confirmed && r.Relation != null)
                    .Sum(r => (long?)r.AmountCents) ?? 0) < t.AmountCents)
            .OrderBy(t => t.TransactedAt)
            .ToListAsync(ct);
    }
}
