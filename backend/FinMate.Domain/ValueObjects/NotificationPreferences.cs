namespace FinMate.Domain.ValueObjects;

public class NotificationPreferences
{
    public bool PushEnabled { get; set; } = true;
    public bool BudgetAlertsEnabled { get; set; } = true;
    public bool MissionRemindersEnabled { get; set; } = true;
}
