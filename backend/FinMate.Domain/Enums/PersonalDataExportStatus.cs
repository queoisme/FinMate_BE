namespace FinMate.Domain.Enums;

/// <summary>Vòng đời một yêu cầu tải toàn bộ dữ liệu cá nhân (Phase 91, BR-EXP-10).</summary>
public enum PersonalDataExportStatus
{
    /// <summary>Đã nhận, chờ job dựng gói.</summary>
    Pending,

    /// <summary>Job đang dựng gói (có hạn thuê <c>LeaseUntil</c>; quá hạn thì job quét chạy lại).</summary>
    Running,

    /// <summary>Gói đã mã hoá và lưu, tải được tới <c>ExpiresAt</c>.</summary>
    Ready,

    /// <summary>Không dựng được; <c>FailureCode</c> nói vì sao.</summary>
    Failed,

    /// <summary>Quá thời hạn giữ file; file đã bị xoá.</summary>
    Expired,
}
