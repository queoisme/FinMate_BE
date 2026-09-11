using FinMate.Application.Common;
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
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .Select(t => new { t.TransactedAt, t.AmountCents, t.TransactionType })
            .ToListAsync(ct);

        return rows
            .GroupBy(t => VietnamTime.DateOf(t.TransactedAt))
            .Select(g => new DailySpendPoint(
                g.Key,
                g.Sum(t => t.TransactionType == TransactionType.Debit ? t.AmountCents : 0L),
                g.Sum(t => t.TransactionType == TransactionType.Credit ? t.AmountCents : 0L),
                g.Count()))
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<List<CategorySpend>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (start, end) = VietnamTime.DayRange(from, to);

        var rows = await _context.Transactions
            .Where(t => t.UserId == userId
                && t.Status == TransactionStatus.Confirmed
                && t.TransactionType == TransactionType.Debit
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .GroupBy(t => new { t.CategoryId, Name = t.Category!.Name, Slug = t.Category!.Slug })
            .Select(g => new CategorySpend(
                g.Key.CategoryId,
                g.Key.Name,
                g.Key.Slug,
                g.Sum(t => t.AmountCents)))
            .ToListAsync(ct);

        return rows.OrderByDescending(r => r.SpentCents).ToList();
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
                && t.TransactedAt >= start
                && t.TransactedAt < end)
            .OrderBy(t => t.TransactedAt)
            .ToListAsync(ct);
    }
}
