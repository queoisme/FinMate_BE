using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một yêu cầu tải toàn bộ dữ liệu cá nhân (Phase 91, BR-EXP-06…12). Chỉ giữ siêu dữ liệu; file ZIP đã mã hoá nằm ở
/// <see cref="PersonalDataExportArchive"/> (bảng riêng, để đọc trạng thái không kéo theo cả file).
/// </summary>
public class PersonalDataExport
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public PersonalDataExportStatus Status { get; set; } = PersonalDataExportStatus.Pending;

    /// <summary>Phiên bản định dạng gói (README ghi lại), tăng khi đổi cấu trúc file.</summary>
    public int FormatVersion { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Mã lý do thất bại cố định (không chứa nội dung lỗi), xem <c>DataExportFailureCodes</c>.</summary>
    public string? FailureCode { get; set; }

    /// <summary>File bị xoá sau thời điểm này (<c>ReadyAt + fileRetentionHours</c>).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>Số lần job đã nhận việc này (giới hạn số lần thử lại).</summary>
    public int Attempts { get; set; }

    /// <summary>Job đang chạy giữ việc tới lúc này; quá hạn mà vẫn <c>Running</c> thì coi như job đã chết.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>SHA-256 hex của token tải đang sống. Null = không có link nào dùng được (chưa xin, đã dùng, hoặc đã hết hạn file).</summary>
    public string? DownloadTokenHash { get; set; }
    public DateTimeOffset? DownloadTokenExpiresAt { get; set; }
    public int DownloadCount { get; set; }
    public DateTimeOffset? LastDownloadedAt { get; set; }

    /// <summary>D14: false khi thất bại do hệ thống, để yêu cầu này không chiếm hạn mức trong 24 giờ.</summary>
    public bool CountsTowardLimit { get; set; } = true;
}

/// <summary>Gói ZIP đã mã hoá AES-256-GCM (khoá chủ <c>SECRETS_MASTER_KEY</c>, dữ liệu kèm là id yêu cầu).</summary>
public class PersonalDataExportArchive
{
    public Guid ExportId { get; set; }
    public byte[] Data { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
