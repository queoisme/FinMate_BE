using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Models;

public record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    long? MonthlyIncomeCents,
    bool PushEnabled,
    bool BudgetAlertsEnabled,
    bool MissionRemindersEnabled,
    bool TermsAccepted,
    bool AiConsentGranted,
    bool TrainingConsentGranted,
    bool SurveyAiConsentGranted = false,
    string OnboardingStatus = "pending")
{
    public static UserProfileDto From(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.MonthlyIncomeCents,
        user.NotificationPrefs.PushEnabled,
        user.NotificationPrefs.BudgetAlertsEnabled,
        user.NotificationPrefs.MissionRemindersEnabled,
        user.TermsAccepted,
        user.AiConsentGranted,
        user.TrainingConsentGranted,
        user.SurveyAiConsentGranted,
        user.OnboardingStatus.ToString().ToLowerInvariant());
}
