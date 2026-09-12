using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Commands;

/// <param name="Code">Chỉ nhận để TỪ CHỐI khi khác giá trị hiện tại.</param>
/// <param name="PeriodType">Như trên — chu kỳ đang chạy được tính theo giá trị cũ.</param>
/// <param name="ConditionType">Như trên.</param>
public record UpdateMissionCommand(
    Guid AdminId,
    Guid MissionId,
    string? Code,
    string? Title,
    string? Description,
    MissionPeriodType? PeriodType,
    MissionConditionType? ConditionType,
    int? ConditionTarget,
    int? ExpReward,
    string? IpAddress);
