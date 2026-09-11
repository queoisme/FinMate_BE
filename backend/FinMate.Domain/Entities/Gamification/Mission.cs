using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities.Gamification;

public class Mission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public MissionPeriodType PeriodType { get; set; }
    public MissionConditionType ConditionType { get; set; }
    public int ConditionTarget { get; set; }
    public int ExpReward { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
