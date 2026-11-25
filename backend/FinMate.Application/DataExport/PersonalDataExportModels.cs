using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.DataExport;

/// <summary>Một yêu cầu tải dữ liệu, như app thấy (BR-EXP-10/11). Không có token hay đường dẫn file.</summary>
public sealed record PersonalDataExportDto(
    Guid Id,
    PersonalDataExportStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? FailedAt,
    string? FailureReason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ExpiredAt,
    long? SizeBytes,
    int DownloadCount,
    DateTimeOffset? LastDownloadedAt)
{
    public static PersonalDataExportDto From(PersonalDataExport e) => new(
        e.Id, e.Status, e.RequestedAt, e.StartedAt, e.ReadyAt, e.FailedAt, FailureReasonText(e.FailureCode), e.ExpiresAt, e.ExpiredAt,
        e.SizeBytes, e.DownloadCount, e.LastDownloadedAt);

    /// <summary>Câu cố định cho app hiện; không bao giờ là nội dung lỗi kỹ thuật.</summary>
    public static string? FailureReasonText(string? code) => code switch
    {
        null => null,
        DataExportFailureCodes.ArchiveTooLarge => "Dữ liệu của bạn lớn hơn giới hạn hiện tại. Đội ngũ FinMate đã được báo và sẽ xử lý; bạn có thể gửi yêu cầu lại sau.",
        DataExportFailureCodes.AccountDeleted => "Tài khoản đang chờ xoá nên không tạo được gói dữ liệu.",
        _ => "Chưa tạo được gói dữ liệu do lỗi hệ thống. Bạn có thể gửi yêu cầu lại.",
    };
}

/// <summary>Link tải dùng một lần (BR-EXP-10).</summary>
public sealed record PersonalDataDownloadLinkDto(string Url, DateTimeOffset ExpiresAt);
