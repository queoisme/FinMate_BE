using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Auth.Otp;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.DataExport;

/// <summary>
/// Yêu cầu tải toàn bộ dữ liệu cá nhân (Phase 91, BR-EXP-06/10/11/13). Xác thực lại bằng OTP email mục đích riêng <see cref="OtpPurpose.DataExport"/>.
/// Hạn mức được kiểm TRƯỚC mã, để một yêu cầu bị từ chối vì hạn mức không đốt mất mã. Không bị chặn bởi gói trả phí.
/// </summary>
public sealed class PersonalDataExportService(
    IPersonalDataExportRepository exports,
    IUserRepository users,
    IOtpService otp,
    IEmailSender email,
    IExportArchiveProtector protector,
    ISystemConfigProvider config,
    IAuditLogService audit,
    TimeProvider clock)
{
    private static readonly TimeSpan LimitWindow = TimeSpan.FromHours(24);

    /// <summary>Gửi mã. Đang trong thời gian chờ gửi lại thì không gửi thêm (mã cũ còn dùng được) và không báo lỗi.</summary>
    public async Task SendOtpAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = await EnabledSettingsAsync(ct);
        await EnsureWithinLimitsAsync(userId, settings, ct);
        var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

        var issued = await otp.IssueAsync(user.Email, OtpPurpose.DataExport, ct);
        if (!issued.Issued)
        {
            return;
        }

        var (subject, body) = OtpMessages.DataExport(issued.Code!);
        if (!await email.SendAsync(user.Email, subject, body, ct))
        {
            await otp.ClearCooldownAsync(user.Email, OtpPurpose.DataExport, ct);
            throw new BusinessRuleException(DataExportErrorCodes.OtpEmailFailed, "Không gửi được email chứa mã. Vui lòng thử lại sau ít phút.");
        }
    }

    public async Task<PersonalDataExportDto> RequestAsync(Guid userId, string? code, string? ip, CancellationToken ct = default)
    {
        var settings = await EnabledSettingsAsync(ct);
        await EnsureWithinLimitsAsync(userId, settings, ct);
        var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        await VerifyOtpAsync(user.Email, code, ct);

        var export = new PersonalDataExport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = PersonalDataExportStatus.Pending,
            FormatVersion = TransactionCsvFormat.FormatVersion,
            RequestedAt = clock.GetUtcNow(),
        };
        if (!await exports.TryAddAsync(export, ct))
        {
            throw ActiveRequest();
        }

        await audit.LogAsync(AuditEvents.DataExportRequested, userId, ip, new { exportId = export.Id }, ct);
        return PersonalDataExportDto.From(export);
    }

    public async Task<IReadOnlyList<PersonalDataExportDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var days = (await config.GetAsync(ct)).Document.DataExport.RequestHistoryDays;
        return (await exports.ListAsync(userId, clock.GetUtcNow().AddDays(-days), ct)).Select(PersonalDataExportDto.From).ToList();
    }

    public async Task<PersonalDataExportDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => PersonalDataExportDto.From(await exports.GetAsync(userId, id, ct) ?? throw new NotFoundException("PersonalDataExport", id));

    private async Task<DataExportSettings> EnabledSettingsAsync(CancellationToken ct)
    {
        var document = (await config.GetAsync(ct)).Document;
        TransactionCsvExportService.EnsureEnabled(document);
        if (!protector.Available)
        {
            // D8: không có khoá thì không lưu được gói đã mã hoá, và không bao giờ lưu bản rõ. CSV vẫn chạy.
            throw new BusinessRuleException(DataExportErrorCodes.Unavailable,
                "Tạm thời chưa tạo được gói dữ liệu đầy đủ. Bạn vẫn xuất được danh sách giao dịch (CSV).");
        }

        return document.DataExport;
    }

    private async Task EnsureWithinLimitsAsync(Guid userId, DataExportSettings settings, CancellationToken ct)
    {
        if (await exports.HasActiveAsync(userId, ct))
        {
            throw ActiveRequest();
        }

        var now = clock.GetUtcNow();
        var recent = await exports.CountedRequestTimesSinceAsync(userId, now - LimitWindow, ct);
        if (recent.Count >= settings.MaxRequestsPerDay)
        {
            throw new TooManyRequestsException(DataExportErrorCodes.RateLimited,
                "Bạn đã yêu cầu tải dữ liệu trong 24 giờ qua. Vui lòng thử lại sau.", recent[0] + LimitWindow);
        }
    }

    private async Task VerifyOtpAsync(string userEmail, string? code, CancellationToken ct)
    {
        var result = string.IsNullOrWhiteSpace(code)
            ? OtpVerifyResult.Invalid
            : await otp.VerifyAsync(userEmail, OtpPurpose.DataExport, code.Trim(), ct);
        switch (result)
        {
            case OtpVerifyResult.Valid:
                return;
            case OtpVerifyResult.TooManyAttempts:
                throw new ForbiddenException(DataExportErrorCodes.OtpTooManyAttempts, "Bạn đã nhập sai quá nhiều lần. Vui lòng xin mã mới.");
            default:
                throw new ForbiddenException(DataExportErrorCodes.OtpInvalid, "Mã xác nhận không đúng hoặc đã hết hạn.");
        }
    }

    private static TooManyRequestsException ActiveRequest()
        => new(DataExportErrorCodes.RateLimited, "Bạn đang có một yêu cầu tải dữ liệu chưa xong. Vui lòng chờ yêu cầu đó hoàn tất.", DateTimeOffset.UtcNow.AddMinutes(5));
}
