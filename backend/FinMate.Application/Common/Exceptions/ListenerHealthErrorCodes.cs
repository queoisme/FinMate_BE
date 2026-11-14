namespace FinMate.Application.Common.Exceptions;

public static class ListenerHealthErrorCodes
{
    /// <summary>User đã báo trạng thái cho đủ <c>listenerHealth.maxDevicesPerUser</c> thiết bị khác (D1).</summary>
    public const string TooManyDevices = "LISTENER_TOO_MANY_DEVICES";
}
