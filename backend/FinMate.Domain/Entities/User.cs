using FinMate.Domain.Enums;
using FinMate.Domain.ValueObjects;

namespace FinMate.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public NotificationPreferences NotificationPrefs { get; set; } = new();
    public bool IsLocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<FinancialAccount> FinancialAccounts { get; set; } = new List<FinancialAccount>();
}
