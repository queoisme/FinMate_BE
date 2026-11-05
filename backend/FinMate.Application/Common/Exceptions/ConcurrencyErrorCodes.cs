namespace FinMate.Application.Common.Exceptions;

public static class ConcurrencyErrorCodes
{
    /// <summary>Dữ liệu vừa bị request khác đổi và không có gì được lưu; client gửi lại là an toàn.</summary>
    public const string ConcurrentUpdate = "CONCURRENT_UPDATE";

    public const string ConcurrentUpdateMessage = "Dữ liệu vừa được cập nhật ở nơi khác. Vui lòng thử lại.";
}
