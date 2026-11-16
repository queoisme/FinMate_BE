namespace FinMate.Application.Common.Exceptions;

public static class ReportErrorCodes
{
    /// <summary>Ngày bắt đầu sau ngày kết thúc.</summary>
    public const string InvalidRange = "REPORT_INVALID_RANGE";

    /// <summary>Khoảng ngày vượt giới hạn cho phép của một lần gọi.</summary>
    public const string RangeTooLong = "REPORT_RANGE_TOO_LONG";
}
