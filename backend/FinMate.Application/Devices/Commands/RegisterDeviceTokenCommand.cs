using FinMate.Domain.Enums;

namespace FinMate.Application.Devices.Commands;

/// <param name="Token">Token FCM client vừa nhận. Lấy từ JWT chứ không phải body cho UserId.</param>
public record RegisterDeviceTokenCommand(Guid UserId, string Token, DevicePlatform Platform);
