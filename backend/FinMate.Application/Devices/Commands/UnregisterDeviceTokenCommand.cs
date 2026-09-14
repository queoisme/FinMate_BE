namespace FinMate.Application.Devices.Commands;

public record UnregisterDeviceTokenCommand(Guid UserId, string Token);
