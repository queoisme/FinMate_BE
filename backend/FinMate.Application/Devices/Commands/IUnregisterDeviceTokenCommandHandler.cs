namespace FinMate.Application.Devices.Commands;

public interface IUnregisterDeviceTokenCommandHandler
{
    Task HandleAsync(UnregisterDeviceTokenCommand command, CancellationToken ct = default);
}
