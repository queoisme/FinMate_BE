namespace FinMate.Application.Common.Models;

public record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool PushEnabled,
    bool BudgetAlertsEnabled,
    bool MissionRemindersEnabled);
