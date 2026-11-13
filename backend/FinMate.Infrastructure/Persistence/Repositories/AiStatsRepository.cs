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
            totals?.MaxProcessing,
            await GetDuplicateOutcomesAsync(from, to, ct),
            await GetTransferOutcomesAsync(from, to, ct));
    }

    private const string Unhinted = "unhinted";

    /// <summary>Phase 83 (D19): chuyển ví theo loại. Chỉ số đếm gộp.</summary>
    private async Task<TransferOutcomeStats> GetTransferOutcomesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var suggested = await _context.AiResults
            .AsNoTracking()
            .Where(r => r.RelationHint == NotificationRelationHint.Transfer && r.CreatedAt >= from && r.CreatedAt < to)
            .GroupBy(r => r.TransferKind)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var accepted = await _context.TransactionMergeSnapshots
            .AsNoTracking()
            .Where(s => (s.Kind == MergeSnapshotKind.TransferPair || s.Kind == MergeSnapshotKind.TransferConvert) && s.CreatedAt >= from && s.CreatedAt < to)
            .GroupBy(s => s.TransferKind)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var rejected = await _context.Transactions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.TransferDismissedAt != null && t.TransferDismissedAt >= from && t.TransferDismissedAt < to)
            .Select(t => _context.AiResults.Where(r => r.NotificationLogId == t.NotificationLogId && r.RelationHint == NotificationRelationHint.Transfer)
                .Select(r => r.TransferKind).FirstOrDefault())
            .GroupBy(k => k)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byKind = new Dictionary<string, TransferKindCounts>();
        void Add(TransferKind? kind, int s, int a, int r)
        {
            var key = kind is { } k ? TransferKindColumn.ToColumn(k) : Unhinted;
            var current = byKind.GetValueOrDefault(key) ?? new TransferKindCounts(0, 0, 0);
            byKind[key] = new TransferKindCounts(current.Suggested + s, current.Accepted + a, current.Rejected + r);
        }

        suggested.ForEach(x => Add(x.Key, x.Count, 0, 0));
        accepted.ForEach(x => Add(x.Key, 0, x.Count, 0));
        rejected.ForEach(x => Add(x.Key, 0, 0, x.Count));
        return new TransferOutcomeStats(byKind);
    }

    /// <summary>
    /// Phase 82: luồng bản trùng có làm đúng việc không, theo cặp nguồn. Chỉ trả số đếm gộp: không id, không user, không số tiền.
    /// Bản được trỏ tới hay survivor có thể đã xoá mềm, nên đọc chúng bỏ qua bộ lọc xoá mềm.
    /// </summary>
    private async Task<DuplicateOutcomeStats> GetDuplicateOutcomesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var linked = await _context.Transactions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.DuplicateStatus != null && t.DeletedAt == null && t.CreatedAt >= from && t.CreatedAt < to)
            .Join(_context.Transactions.IgnoreQueryFilters(), t => t.DuplicateOfTransactionId, d => (Guid?)d.Id,
                (t, d) => new { A = t.Source, B = d.Source, t.DuplicateStatus })
            .GroupBy(x => new { x.A, x.B, x.DuplicateStatus })
            .Select(g => new { g.Key.A, g.Key.B, g.Key.DuplicateStatus, Count = g.Count() })
            .ToListAsync(ct);

        var merged = await _context.Transactions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.MergedIntoTransactionId != null && t.DeletedAt >= from && t.DeletedAt < to)
            .Join(_context.Transactions.IgnoreQueryFilters(), t => t.MergedIntoTransactionId, s => (Guid?)s.Id,
                (t, s) => new { A = t.Source, B = s.Source })
            .GroupBy(x => new { x.A, x.B })
            .Select(g => new { g.Key.A, g.Key.B, Count = g.Count() })
            .ToListAsync(ct);

        var dropped = await _context.NotificationLogs
            .AsNoTracking()
            .CountAsync(l => l.Status == NotificationLogStatus.Duplicate && l.CreatedAt >= from && l.CreatedAt < to, ct);

        var byPair = new Dictionary<string, DuplicatePairCounts>();
        void Add(TransactionSource a, TransactionSource b, int suspected, int dismissed, int mergedCount)
        {
            var key = PairKey(a, b);
            var current = byPair.GetValueOrDefault(key) ?? new DuplicatePairCounts(0, 0, 0);
            byPair[key] = new DuplicatePairCounts(current.Suspected + suspected, current.Dismissed + dismissed, current.Merged + mergedCount);
        }

        foreach (var row in linked)
        {
            var suspected = row.DuplicateStatus == DuplicateStatus.Suspected ? row.Count : 0;
            Add(row.A, row.B, suspected, row.Count - suspected, 0);
        }

        foreach (var row in merged)
        {
            Add(row.A, row.B, 0, 0, row.Count);
        }

        return new DuplicateOutcomeStats(byPair, dropped);
    }

    private static string PairKey(TransactionSource a, TransactionSource b)
        => string.Join('+', new[] { a, b }.Select(s => s.ToString().ToLowerInvariant()).Order(StringComparer.Ordinal));
}
