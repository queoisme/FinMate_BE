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
    bool TermsAccepted,
    bool AiConsentGranted,
    bool TrainingConsentGranted,
    bool SurveyAiConsentGranted = false,
    string OnboardingStatus = "pending",
    bool CommunityConsentGranted = false,
    bool ListenerHealthAlertsEnabled = true,
    int? TermsVersion = null,
    string? FinMateId = null,
    bool LoanRemindersEnabled = true)
{
    /// <param name="termsVersion">Phiên bản Điều khoản hiện hành (từ <c>ILegalVersionProvider</c>).</param>
    public static UserProfileDto From(User user, int termsVersion) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.MonthlyIncomeCents,
        user.NotificationPrefs.PushEnabled,
        user.NotificationPrefs.BudgetAlertsEnabled,
        user.HasAcceptedTerms(termsVersion),
        user.AiConsentGranted,
        user.TrainingConsentGranted,
        user.SurveyAiConsentGranted,
        user.OnboardingStatus.ToString().ToLowerInvariant(),
        user.CommunityConsentGranted,
        user.NotificationPrefs.ListenerHealthAlertsEnabled,
        user.TermsVersion,
        user.FinMateId,
        user.NotificationPrefs.LoanRemindersEnabled);
}
