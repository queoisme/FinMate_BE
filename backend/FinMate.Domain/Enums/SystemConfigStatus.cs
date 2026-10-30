namespace FinMate.Domain.Enums;

public enum SystemConfigStatus
{
    /// <summary>Phiên bản đang chạy: mỗi tài liệu có đúng một.</summary>
    Active,

    /// <summary>Phiên bản cũ, còn trong lịch sử để xem hoặc hoàn tác.</summary>
    Archived,
}
