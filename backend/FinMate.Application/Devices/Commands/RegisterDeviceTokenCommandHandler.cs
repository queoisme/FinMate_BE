using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Devices.Commands;

public class RegisterDeviceTokenCommandHandler : IRegisterDeviceTokenCommandHandler
{
    private readonly IDeviceTokenRepository _deviceTokenRepository;
    private readonly IValidator<RegisterDeviceTokenCommand> _validator;

    public RegisterDeviceTokenCommandHandler(
        IDeviceTokenRepository deviceTokenRepository,
        IValidator<RegisterDeviceTokenCommand> validator)
    {
        _deviceTokenRepository = deviceTokenRepository;
        _validator = validator;
    }

    public async Task HandleAsync(RegisterDeviceTokenCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var now = DateTimeOffset.UtcNow;
        var existing = await _deviceTokenRepository.GetByTokenAsync(command.Token, ct);

        if (existing is null)
        {
            await _deviceTokenRepository.AddAsync(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = command.UserId,
                Token = command.Token,
                Platform = command.Platform,
                CreatedAt = now,
                LastSeenAt = now,
            }, ct);
            return;
        }

        // Token đã tồn tại. Hai ca, cùng một cách xử lý — CHUYỂN CHỦ, không thêm dòng mới:
        //
        // - cùng user mở lại app: chỉ cần chạm LastSeenAt;
        // - user khác đăng nhập trên chính máy đó: dòng cũ phải đổi chủ, nếu không thì thông
        //   báo tài chính của người mới vẫn đẩy xuống đúng máy đó cho người cũ đọc.
        //
        // Client gọi đăng ký mỗi lần mở app nên nhánh này chạy thường xuyên — nó là đường
        // chính, không phải ca hiếm.
        existing.UserId = command.UserId;
        existing.Platform = command.Platform;
        existing.LastSeenAt = now;
        await _deviceTokenRepository.UpdateAsync(existing, ct);
    }
}
