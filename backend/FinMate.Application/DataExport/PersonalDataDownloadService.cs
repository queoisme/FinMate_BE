using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.DataExport;

/// <summary>Gói đã giải mã, sẵn sàng trả về.</summary>
public sealed record PersonalDataDownload(byte[] Content, string FileName);

/// <summary>
/// Link tải dùng một lần (Phase 91, BR-EXP-10, D7, D9). Token 32 byte ngẫu nhiên, DB chỉ giữ SHA-256; link gắn với user (endpoint tải vẫn đòi đăng nhập)
/// và chết ngay khi bắt đầu tải, khi hết <c>downloadLinkMinutes</c>, khi có link mới, hoặc khi file hết hạn. Không log token hay link.
/// </summary>
public sealed class PersonalDataDownloadService(
    IPersonalDataExportRepository exports,
    IExportArchiveProtector protector,
    ISystemConfigProvider config,
    IAuditLogService audit,
    TimeProvider clock)
{
    public async Task<PersonalDataDownloadLinkDto> IssueLinkAsync(Guid userId, Guid id, string? ip, CancellationToken ct = default)
    {
        var document = (await config.GetAsync(ct)).Document;
        TransactionCsvExportService.EnsureEnabled(document);
        var export = await exports.GetAsync(userId, id, ct) ?? throw new NotFoundException("PersonalDataExport", id);
        var now = clock.GetUtcNow();
        EnsureDownloadable(export.Status, export.ExpiresAt, now);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var linkExpires = Min(now.AddMinutes(document.DataExport.DownloadLinkMinutes), export.ExpiresAt!.Value);
        if (!await exports.SetDownloadTokenAsync(userId, id, Hash(token), linkExpires, ct))
        {
            throw Expired();
        }

        await audit.LogAsync(AuditEvents.DataExportLinkIssued, userId, ip, new { exportId = id }, ct);
        return new PersonalDataDownloadLinkDto($"/api/v1/exports/personal-data/{id}/download?token={token}", linkExpires);
    }

    public async Task<PersonalDataDownload> DownloadAsync(Guid userId, Guid id, string? token, string? ip, CancellationToken ct = default)
    {
        var export = await exports.GetAsync(userId, id, ct) ?? throw new NotFoundException("PersonalDataExport", id);
        var now = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(token) || !await exports.TryConsumeDownloadTokenAsync(userId, id, Hash(token.Trim()), now, ct))
        {
            throw export.Status == PersonalDataExportStatus.Expired ? Expired() : LinkExpired();
        }

        var archive = await exports.GetArchiveAsync(id, ct);
        var content = archive is null ? null : protector.Unprotect(id, archive.Data, archive.Nonce);
        if (content is null)
        {
            throw Expired();
        }

        await audit.LogAsync(AuditEvents.DataExportDownloaded, userId, ip, new { exportId = id }, ct);
        var day = VietnamTime.DateOf(export.ReadyAt ?? now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new PersonalDataDownload(content, $"finmate-du-lieu-{day}.zip");
    }

    private static void EnsureDownloadable(PersonalDataExportStatus status, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (status == PersonalDataExportStatus.Expired || (status == PersonalDataExportStatus.Ready && expiresAt <= now))
        {
            throw Expired();
        }

        if (status != PersonalDataExportStatus.Ready)
        {
            throw new ConflictException(DataExportErrorCodes.NotReady, "Gói dữ liệu chưa sẵn sàng để tải.");
        }
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static GoneException Expired() => new(DataExportErrorCodes.Expired, "Gói dữ liệu đã hết hạn và bị xoá. Bạn có thể gửi yêu cầu mới.");

    private static GoneException LinkExpired() => new(DataExportErrorCodes.LinkExpired, "Link tải đã được dùng hoặc đã hết hạn. Hãy lấy link mới trong app.");
}
