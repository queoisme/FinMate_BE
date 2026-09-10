using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

public class ProviderConfig
{
    public Guid Id { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
