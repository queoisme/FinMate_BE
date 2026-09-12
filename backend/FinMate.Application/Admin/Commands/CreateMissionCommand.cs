using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Commands;

public record CreateMissionCommand(
    Guid AdminId,
    string Code,
    string Title,
    string Description,
    MissionPeriodType PeriodType,
    MissionConditionType ConditionType,
    int ConditionTarget,
    int ExpReward,
    string? IpAddress);
