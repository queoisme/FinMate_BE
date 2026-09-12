namespace FinMate.Application.Admin.Commands;

public record SetSystemCategoryActivationCommand(
    Guid AdminId,
    Guid CategoryId,
    bool IsActive,
    string? IpAddress);
