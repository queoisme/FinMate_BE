namespace FinMate.Domain.Enums;

/// <summary>Tình trạng khảo sát đầu vào của tài khoản. Tài khoản có sẵn trước tính năng này được coi là <see cref="Completed"/>.</summary>
public enum OnboardingStatus
{
    Pending,
    Completed,
    Skipped,
}
