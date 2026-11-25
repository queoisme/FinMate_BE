using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport.Package;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.DataExport;

/// <summary>
/// Dựng một gói dữ liệu (Phase 91, BR-EXP-07/09/10) và quét việc bị bỏ dở. Một yêu cầu đi: nhận việc (thuê có hạn) → dựng ZIP trong bộ nhớ, dừng khi quá
/// <c>maxArchiveMb</c> → mã hoá → lưu và chuyển <c>Ready</c> trong một transaction → audit, push, email. Lỗi tạm thời trả việc về <c>Pending</c> cho job quét;
/// hết <see cref="MaxAttempts"/> lượt thì <c>Failed</c>. Thất bại do hệ thống không tính vào hạn mức (D14). Cờ tắt thì không nhận việc (D15).
/// Không log nội dung, số tiền hay id user.
/// </summary>
public sealed class PersonalDataExportRunner(
    IPersonalDataExportRepository exports,
    IUserRepository users,
    PersonalDataArchiveBuilder builder,
    IExportArchiveProtector protector,
    ISystemConfigProvider config,
    IAuditLogService audit,
    IIncidentService incidents,
    PersonalDataExportNotifier notifier,
    IPersonalDataExportQueue queue,
    TimeProvider clock,
    ILogger<PersonalDataExportRunner> logger)
{
    public const int MaxAttempts = 3;
    public const string TooLargeFingerprint = "data_export:archive_too_large";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LostEnqueueAfter = TimeSpan.FromMinutes(2);

    public async Task RunAsync(Guid exportId, CancellationToken ct = default)
    {
        var settings = await EnabledSettingsAsync(ct);
        if (settings is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var export = await exports.TryLeaseAsync(exportId, now, now + Lease, MaxAttempts, ct);
        if (export is null)
        {
            return;
        }

        if (await users.GetByIdAsync(export.UserId, ct) is null)
        {
            // Tài khoản đã xin xoá (bị xoá mềm) giữa lúc chờ: không dựng gói, không báo (không ai đăng nhập được để xem).
            await FailAsync(export, DataExportFailureCodes.AccountDeleted, notify: false, ct);
            return;
        }

        await BuildAndStoreAsync(export, settings, ct);
    }

    /// <summary>Job quét: chạy lại việc bị mất hoặc bỏ dở; việc đã hết lượt thì đánh dấu thất bại.</summary>
    public async Task SweepAsync(CancellationToken ct = default)
    {
        if (await EnabledSettingsAsync(ct) is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var export in await exports.FindStaleAsync(now - LostEnqueueAfter, now, ct))
        {
            if (export.Attempts >= MaxAttempts)
            {
                await FailAsync(export, DataExportFailureCodes.InternalError, notify: true, ct);
            }
            else
            {
                queue.Enqueue(export.Id);
            }
        }
    }

    private async Task BuildAndStoreAsync(PersonalDataExport export, DataExportSettings settings, CancellationToken ct)
    {
        try
        {
            using var buffer = new MemoryStream();
            var result = await builder.BuildAsync(export.UserId, buffer, settings.MaxArchiveMb * 1024L * 1024L, clock.GetUtcNow(), ct);
            var (ciphertext, nonce) = protector.Protect(export.Id, buffer.ToArray());
            var readyAt = clock.GetUtcNow();
            if (await exports.MarkReadyAsync(export.Id, ciphertext, nonce, result.SizeBytes, readyAt, readyAt.AddHours(settings.FileRetentionHours), ct))
            {
                await audit.LogAsync(AuditEvents.DataExportReady, export.UserId, null, new { exportId = export.Id }, ct);
                await notifier.ReadyAsync(export.UserId, export.Id, ct);
            }
        }
        catch (ArchiveTooLargeException)
        {
            await FailAsync(export, DataExportFailureCodes.ArchiveTooLarge, notify: true, ct);
            await incidents.RaiseAsync(new IncidentSignal(TooLargeFingerprint, IncidentSource.Job, "data-export", IncidentSeverity.Normal,
                "Có gói dữ liệu cá nhân vượt giới hạn kích thước", $"maxArchiveMb = {settings.MaxArchiveMb}"), occurrences: 1, clock.GetUtcNow(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Dựng gói dữ liệu lỗi (lần {Attempt}/{Max}): {Error}", export.Attempts, MaxAttempts, ex.GetType().Name);
            if (export.Attempts >= MaxAttempts)
            {
                await FailAsync(export, DataExportFailureCodes.InternalError, notify: true, ct);
            }
            else
            {
                await exports.ReleaseAsync(export.Id, ct);
            }
        }
    }

    private async Task FailAsync(PersonalDataExport export, string code, bool notify, CancellationToken ct)
    {
        if (!await exports.MarkFailedAsync(export.Id, code, countsTowardLimit: false, clock.GetUtcNow(), ct))
        {
            return;
        }

        await audit.LogAsync(AuditEvents.DataExportFailed, export.UserId, null, new { exportId = export.Id, failureCode = code }, ct);
        if (notify)
        {
            await notifier.FailedAsync(export.UserId, export.Id, ct);
        }
    }

    private async Task<DataExportSettings?> EnabledSettingsAsync(CancellationToken ct)
    {
        var document = (await config.GetAsync(ct)).Document;
        return document.Features.DataExport ? document.DataExport : null;
    }
}
