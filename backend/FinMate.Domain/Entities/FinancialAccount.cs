using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class FinancialAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProviderConfigId { get; set; }
    public AccountType AccountType { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? PackageName { get; set; }
    public bool IsMonitored { get; set; } = true;
    public long BalanceCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public ProviderConfig? ProviderConfig { get; set; }
}
