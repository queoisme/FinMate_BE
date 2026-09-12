namespace FinMate.Application.Admin.Commands;

public record SetProviderConfigActivationCommand(
    Guid AdminId,
    Guid ProviderConfigId,
    bool IsActive,
    string? IpAddress);
