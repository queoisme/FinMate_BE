using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Devices.Commands;

public class UnregisterDeviceTokenCommandHandler : IUnregisterDeviceTokenCommandHandler
{
    private readonly IDeviceTokenRepository _deviceTokenRepository;

    public UnregisterDeviceTokenCommandHandler(IDeviceTokenRepository deviceTokenRepository)
    {
        _deviceTokenRepository = deviceTokenRepository;
    }

    /// <summary>
    /// Gọi khi đăng xuất. Lọc theo userId nên không ai gỡ được thiết bị của người khác, và
    /// token không tồn tại thì lặng lẽ không làm gì — đăng xuất phải luôn thành công, còn
    /// việc phân biệt "đã gỡ rồi" với "chưa từng có" chỉ tổ lộ token nào đang tồn tại.
    /// </summary>
    public Task HandleAsync(UnregisterDeviceTokenCommand command, CancellationToken ct = default)
        => _deviceTokenRepository.RemoveAsync(command.UserId, command.Token, ct);
}
