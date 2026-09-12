using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class AiStatsRepository : IAiStatsRepository
{
    private readonly FinMateDbContext _context;

    public AiStatsRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<AiProductionStats> GetProductionStatsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = _context.AiResults
            .AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to);

        var totals = await results
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Duplicates = g.Count(r => r.IsPotentialDuplicate),
                AvgClassifier = g.Average(r => r.ClassifierConfidence),
                AvgExtraction = g.Average(r => r.ExtractionConfidence),
                AvgCategorization = g.Average(r => r.CategorizationConfidence),
                AvgProcessing = g.Average(r => (double?)r.ProcessingMs),
                MaxProcessing = g.Max(r => r.ProcessingMs),
            })
            .FirstOrDefaultAsync(ct);

        var byPipelineResult = await results
            .GroupBy(r => r.PipelineResult)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Gộp theo provider để thấy ngân hàng nào đang khiến pipeline vất vả nhất.
        var byPackage = await results
            .Where(r => r.NotificationLog != null)
            .GroupBy(r => r.NotificationLog!.PackageName)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Version null = kết quả sinh ra khi chưa promote model nào, tức là nhánh luật.
        var byClassifierVersion = await results
            .GroupBy(r => r.ClassifierVersion)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var draftsCreated = await _context.Transactions
            .AsNoTracking()
            .CountAsync(
                t => t.Source == TransactionSource.Notification
                    && t.CreatedAt >= from && t.CreatedAt < to,
                ct);

        var draftsConfirmed = await _context.Transactions
            .AsNoTracking()
            .CountAsync(
                t => t.Source == TransactionSource.Notification
                    && t.Status == TransactionStatus.Confirmed
                    && t.CreatedAt >= from && t.CreatedAt < to,
                ct);

        // Nối transaction với chính dự đoán đã sinh ra nó qua notification_log_id, rồi so slug
        // danh mục hiện tại với slug AI đã đoán. Chỉ đếm giao dịch ĐÃ CONFIRM: giao dịch còn ở
        // trạng thái nháp thì người dùng chưa nói gì cả, đếm vào sẽ thổi phồng tỉ lệ sai.
        //
        // Method syntax chứ không phải query syntax: `from` là từ khoá ngữ cảnh của LINQ query
        // expression nên tham số tên `from` không dùng được bên trong.
        var judged = _context.Transactions
            .AsNoTracking()
            .Where(t => t.Source == TransactionSource.Notification
                && t.Status == TransactionStatus.Confirmed)
            .Join(
                _context.AiResults.AsNoTracking()
                    .Where(r => r.CategorySlug != null && r.CreatedAt >= from && r.CreatedAt < to),
                t => t.NotificationLogId,
                r => (Guid?)r.NotificationLogId,
                (t, r) => new
                {
                    PredictedSlug = r.CategorySlug,
                    ActualSlug = t.Category != null ? t.Category.Slug : null,
                });

        var categorised = await judged.CountAsync(ct);
        var corrections = await judged.CountAsync(x => x.ActualSlug != x.PredictedSlug, ct);

        return new AiProductionStats(
            totals?.Total ?? 0,
            byPipelineResult.ToDictionary(x => x.Key.ToString(), x => x.Count),
            byPackage.ToDictionary(x => x.Key, x => x.Count),
            byClassifierVersion.ToDictionary(x => x.Key ?? "rule", x => x.Count),
            totals?.Duplicates ?? 0,
            draftsCreated,
            draftsConfirmed,
            corrections,
            categorised,
            totals?.AvgClassifier,
            totals?.AvgExtraction,
            totals?.AvgCategorization,
            totals?.AvgProcessing,
            totals?.MaxProcessing);
    }
}
