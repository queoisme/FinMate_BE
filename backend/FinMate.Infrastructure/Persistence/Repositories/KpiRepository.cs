using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FinMate.Infrastructure.Persistence.Repositories;

/// <summary>Số liệu tổng hợp cho dashboard KPI; SQL ở <see cref="KpiSql"/>.</summary>
public class KpiRepository : IKpiRepository
{
    private readonly FinMateDbContext _context;

    public KpiRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<KpiDailyRow>> GetDailyCountsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var rows = await _context.Database.SqlQueryRaw<DailyRow>(KpiSql.Daily, Param("from", from.ToUniversalTime()), Param("to", to.ToUniversalTime())).ToListAsync(ct);
        return rows.Select(r => new KpiDailyRow(r.Day, r.Metric, r.Key, r.Value)).ToList();
    }

    public async Task<IReadOnlyList<long>> CountTransactingUsersAsync(IReadOnlyList<KpiTimeWindow> windows, CancellationToken ct = default)
    {
        if (windows.Count == 0)
        {
            return [];
        }

        var starts = windows.Select(w => w.Start.ToUniversalTime()).ToArray();
        var ends = windows.Select(w => w.End.ToUniversalTime()).ToArray();
        var rows = await _context.Database.SqlQueryRaw<IndexedCountRow>(KpiSql.TransactingUsers, Param("starts", starts), Param("ends", ends)).ToListAsync(ct);
        var byIndex = rows.ToDictionary(r => r.Index, r => r.Value);
        return Enumerable.Range(1, windows.Count).Select(i => byIndex.GetValueOrDefault(i)).ToList();
    }

    public async Task<IReadOnlyList<KpiPayingSnapshot>> CountPayingAsync(IReadOnlyList<DateTimeOffset> instants, CancellationToken ct = default)
    {
        if (instants.Count == 0)
        {
            return [];
        }

        var at = instants.Select(i => i.ToUniversalTime()).ToArray();
        var rows = await _context.Database.SqlQueryRaw<IndexedCountRow>(KpiSql.Paying, Param("at", at)).ToListAsync(ct);
        return Enumerable.Range(1, instants.Count).Select(i =>
        {
            var mine = rows.Where(r => r.Index == i).ToList();
            var total = mine.Where(r => r.Key is null).Select(r => r.Value).FirstOrDefault();
            var byPlan = mine.Where(r => r.Key is not null).ToDictionary(r => r.Key!, r => r.Value, StringComparer.Ordinal);
            return new KpiPayingSnapshot(total, byPlan);
        }).ToList();
    }

    public async Task<KpiFeatureUserCounts> CountFeatureUsersAsync(CancellationToken ct = default)
    {
        var row = await _context.Database.SqlQueryRaw<FeatureRow>(KpiSql.FeatureUsers).SingleAsync(ct);
        return new KpiFeatureUserCounts(row.Budgets, row.Goals, row.Loans, row.Groups);
    }

    private static NpgsqlParameter Param(string name, DateTimeOffset value) => new(name, NpgsqlDbType.TimestampTz) { Value = value };

    private static NpgsqlParameter Param(string name, DateTimeOffset[] value) => new(name, NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = value };

    private sealed class DailyRow
    {
        public DateOnly Day { get; set; }
        public string Metric { get; set; } = string.Empty;
        public string? Key { get; set; }
        public long Value { get; set; }
    }

    private sealed class IndexedCountRow
    {
        public int Index { get; set; }
        public string? Key { get; set; }
        public long Value { get; set; }
    }

    private sealed class FeatureRow
    {
        public long Budgets { get; set; }
        public long Goals { get; set; }
        public long Loans { get; set; }
        public long Groups { get; set; }
    }
}
