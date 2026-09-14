namespace FinMate.Application.Devices.Commands;

public interface IRegisterDeviceTokenCommandHandler
{
    Task HandleAsync(RegisterDeviceTokenCommand command, CancellationToken ct = default);
}
