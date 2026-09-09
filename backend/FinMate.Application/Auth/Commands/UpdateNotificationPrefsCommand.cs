namespace FinMate.Application.Auth.Commands;

public record UpdateNotificationPrefsCommand(
    Guid UserId,
    bool PushEnabled,
    bool BudgetAlertsEnabled,
    bool MissionRemindersEnabled);
