namespace FinMate.Application.Admin.Commands;

public record SetMissionActivationCommand(
    Guid AdminId,
    Guid MissionId,
    bool IsActive,
    string? IpAddress);
