using FinMate.Application.Admin.SystemConfig;
using FinMate.Domain.Entities;

namespace FinMate.Application.ListenerHealth;

public enum DeviceHealthState
{
    Ok,
    PermissionLost,

    /// <summary>Quá <c>staleReportHours</c> không báo: app cũ, đã gỡ hoặc bị diệt hẳn. Không nhắc (D8), chỉ hiện ở độ phủ.</summary>
    Unknown,
}

/// <summary>
/// BR-LH-04: trạng thái hiện tại của một thiết bị cho việc nhắc. Lần báo đã cũ thì không tin được nữa, kể cả khi nó nói mất quyền.
/// Mất kết nối mà còn quyền vẫn là <c>Ok</c> ở đây (app tự <c>requestRebind</c>); độ phủ thì vẫn tính ngày đó không ok theo S3.
/// </summary>
public static class DeviceHealthEvaluator
{
    public static DeviceHealthState Evaluate(DeviceListenerStatus status, DateTimeOffset now, ListenerHealthSettings settings)
    {
        if ((now - status.LastReportedAt).TotalHours > settings.StaleReportHours)
        {
            return DeviceHealthState.Unknown;
        }

        return status.PermissionGranted ? DeviceHealthState.Ok : DeviceHealthState.PermissionLost;
    }
}
