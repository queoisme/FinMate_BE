namespace FinMate.Application.Common.Exceptions;

/// <summary>Phase 91: mã lỗi của xuất dữ liệu. Câu tiếng Việt đi kèm do nơi ném quyết định; app chỉ hiện <c>message</c>.</summary>
public static class DataExportErrorCodes
{
    /// <summary>422: tắt bằng <c>features.dataExport</c>.</summary>
    public const string Disabled = "DATA_EXPORT_DISABLED";

    /// <summary>422 (D8): thiếu <c>SECRETS_MASTER_KEY</c>, không lưu được gói đã mã hoá. CSV vẫn chạy.</summary>
    public const string Unavailable = "DATA_EXPORT_UNAVAILABLE";

    /// <summary>422 (BR-EXP-01): khoảng ngày hoặc số dòng vượt giới hạn.</summary>
    public const string RangeTooLarge = "EXPORT_RANGE_TOO_LARGE";

    /// <summary>429 (BR-EXP-06, S3): quá số yêu cầu gói trong 24 giờ, đang có yêu cầu chạy, hoặc quá số file CSV mỗi giờ.</summary>
    public const string RateLimited = "EXPORT_RATE_LIMITED";

    /// <summary>403 (BR-EXP-06): thiếu mã, sai, hết hạn hoặc đã dùng.</summary>
    public const string OtpInvalid = "EXPORT_OTP_INVALID";

    /// <summary>403: nhập sai quá số lần, mã đã bị huỷ.</summary>
    public const string OtpTooManyAttempts = "EXPORT_OTP_TOO_MANY_ATTEMPTS";

    /// <summary>422: không gửi được email chứa mã.</summary>
    public const string OtpEmailFailed = "EXPORT_OTP_EMAIL_FAILED";

    /// <summary>409: gói chưa xong (hoặc đã thất bại), chưa xin link được.</summary>
    public const string NotReady = "EXPORT_NOT_READY";

    /// <summary>410: file đã quá hạn giữ và bị xoá.</summary>
    public const string Expired = "EXPORT_EXPIRED";

    /// <summary>410 (BR-EXP-10): link đã dùng, đã bị link mới thay, hoặc quá hạn.</summary>
    public const string LinkExpired = "EXPORT_LINK_EXPIRED";

    /// <summary>409 (S5): xin xoá tài khoản khi đang có yêu cầu xuất <c>Pending</c>/<c>Running</c>.</summary>
    public const string InProgress = "DATA_EXPORT_IN_PROGRESS";
}

/// <summary>Lý do thất bại lưu ở <c>personal_data_exports.failure_code</c>. Cố định, không chứa nội dung lỗi.</summary>
public static class DataExportFailureCodes
{
    public const string ArchiveTooLarge = "archive_too_large";
    public const string AccountDeleted = "account_deleted";
    public const string InternalError = "internal_error";
}
