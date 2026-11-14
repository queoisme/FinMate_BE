using FinMate.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ProviderFormatRepository : IProviderFormatRepository
{
    private readonly FinMateDbContext _context;

    public ProviderFormatRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProviderFormatCounts>> CountAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var end = now.ToUniversalTime();
        var recentStart = end.AddHours(-24);
        var baselineStart = end.AddDays(-8);
        var rows = await _context.Database.SqlQuery<Row>($"""
            SELECT p.provider_key AS "ProviderKey", min(p.display_name) AS "DisplayName",
                   count(*) FILTER (WHERE a.created_at >= {recentStart})::int AS "RecentTotal",
                   count(*) FILTER (WHERE a.created_at >= {recentStart} AND a.pipeline_result IN ('extraction_failed', 'uncertain'))::int AS "RecentFailed",
                   count(*) FILTER (WHERE a.created_at >= {recentStart} AND a.pipeline_result = 'financial')::int AS "RecentFinancial",
                   count(*) FILTER (WHERE a.created_at < {recentStart})::int AS "BaselineTotal",
                   count(*) FILTER (WHERE a.created_at < {recentStart} AND a.pipeline_result IN ('extraction_failed', 'uncertain'))::int AS "BaselineFailed",
                   count(*) FILTER (WHERE a.created_at < {recentStart} AND a.pipeline_result = 'financial')::int AS "BaselineFinancial"
            FROM ai_results a
            JOIN notification_logs n ON n.id = a.notification_log_id
            JOIN provider_configs p ON p.package_name = n.package_name
            WHERE a.created_at >= {baselineStart} AND a.created_at < {end} AND a.pipeline_result <> 'error'
            GROUP BY p.provider_key
            ORDER BY p.provider_key
            """).ToListAsync(ct);
        return rows.Select(r => new ProviderFormatCounts(
            r.ProviderKey, r.DisplayName, r.RecentTotal, r.RecentFailed, r.RecentFinancial, r.BaselineTotal, r.BaselineFailed, r.BaselineFinancial)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountUploadGapsAsync(DateOnly fromDay, CancellationToken ct = default)
        => await _context.ListenerUploadGapDaily
            .Where(g => g.Day >= fromDay)
            .GroupBy(g => g.AppVersion)
            .Select(g => new { g.Key, Count = g.Sum(x => x.Count) })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    private sealed class Row
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int RecentTotal { get; set; }
        public int RecentFailed { get; set; }
        public int RecentFinancial { get; set; }
        public int BaselineTotal { get; set; }
        public int BaselineFailed { get; set; }
        public int BaselineFinancial { get; set; }
    }
}
