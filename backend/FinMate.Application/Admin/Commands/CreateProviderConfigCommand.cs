using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Commands;

public record CreateProviderConfigCommand(
    Guid AdminId,
    string ProviderKey,
    string DisplayName,
    string PackageName,
    AccountType AccountType,
    string? IpAddress);
