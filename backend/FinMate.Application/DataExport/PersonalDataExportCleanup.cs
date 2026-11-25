using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.DataExport;

/// <summary>
/// Dọn dẹp gói dữ liệu (Phase 91, BR-EXP-09/11): quá <c>fileRetentionHours</c> → <c>Expired</c>, file bị xoá, link chết, ghi audit; dòng lịch sử quá
/// <c>requestHistoryDays</c> bị xoá. Chạy cả khi cờ tắt (D15): tắt tính năng không được làm file nằm lại lâu hơn hạn.
/// </summary>
public sealed class PersonalDataExportCleanup(
    IPersonalDataExportRepository exports,
    ISystemConfigProvider config,
    IAuditLogService audit,
    TimeProvider clock,
    ILogger<PersonalDataExportCleanup> logger)
{
    private const int Batch = 200;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var expired = 0;
        foreach (var export in await exports.FindDueForExpiryAsync(now, Batch, ct))
        {
            if (await exports.ExpireAsync(export.Id, now, ct))
            {
                expired++;
                await audit.LogAsync(AuditEvents.DataExportExpired, export.UserId, null, new { exportId = export.Id }, ct);
            }
        }

        var days = (await config.GetAsync(ct)).Document.DataExport.RequestHistoryDays;
        var purged = await exports.PurgeHistoryAsync(now.AddDays(-days), ct);
        if (expired > 0 || purged > 0)
        {
            logger.LogInformation("Dọn gói dữ liệu: {Expired} hết hạn, {Purged} dòng lịch sử đã xoá", expired, purged);
        }
    }
}
