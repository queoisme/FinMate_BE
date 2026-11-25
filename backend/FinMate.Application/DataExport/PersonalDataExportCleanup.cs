using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.DataExport;

/// <summary>
/// Dọn dẹp gói dữ liệu (Phase 91, BR-EXP-09/11): quá <c>fileRetentionHours</c> → <c>Expired</c>, file bị xoá, link chết, ghi audit; dòng lịch sử quá
/// <c>requestHistoryDays</c> bị xoá. Chạy cả khi cờ tắt (D15): tắt tính năng không được làm file nằm lại lâu hơn hạn. Còn yêu cầu chưa xong quá
/// <see cref="OverdueAfter"/> thì mở sự cố Normal <see cref="OverdueFingerprint"/> (hạn luật là 10 ngày), hết thì tự đóng.
/// </summary>
public sealed class PersonalDataExportCleanup(
    IPersonalDataExportRepository exports,
    ISystemConfigProvider config,
    IAuditLogService audit,
    IIncidentService incidents,
    TimeProvider clock,
    ILogger<PersonalDataExportCleanup> logger)
{
    public const string OverdueFingerprint = "data_export:overdue";
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromDays(5);
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

        await CheckOverdueAsync(now, ct);

        var days = (await config.GetAsync(ct)).Document.DataExport.RequestHistoryDays;
        var purged = await exports.PurgeHistoryAsync(now.AddDays(-days), ct);
        if (expired > 0 || purged > 0)
        {
            logger.LogInformation("Dọn gói dữ liệu: {Expired} hết hạn, {Purged} dòng lịch sử đã xoá", expired, purged);
        }
    }

    private async Task CheckOverdueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var overdue = await exports.CountUnfinishedSinceBeforeAsync(now - OverdueAfter, ct);
        if (overdue > 0)
        {
            await incidents.RaiseAsync(new IncidentSignal(OverdueFingerprint, IncidentSource.Job, "data-export", IncidentSeverity.Normal,
                "Có yêu cầu tải dữ liệu cá nhân chưa xong sau 5 ngày", $"{overdue} yêu cầu; hạn luật là 10 ngày kể từ lúc yêu cầu."), overdue, now, ct);
        }
        else
        {
            await incidents.ResolveAsync(OverdueFingerprint, now, ct);
        }
    }
}
