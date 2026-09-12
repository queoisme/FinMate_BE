using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public class SetProviderConfigActivationCommandHandler : ISetProviderConfigActivationCommandHandler
{
    private readonly IProviderConfigRepository _providerConfigRepository;
    private readonly IAuditLogService _auditLogService;

    public SetProviderConfigActivationCommandHandler(
        IProviderConfigRepository providerConfigRepository,
        IAuditLogService auditLogService)
    {
        _providerConfigRepository = providerConfigRepository;
        _auditLogService = auditLogService;
    }

    public async Task<ProviderConfigDto> HandleAsync(
        SetProviderConfigActivationCommand command, CancellationToken ct = default)
    {
        var config = await _providerConfigRepository.GetByIdAsync(command.ProviderConfigId, ct)
            ?? throw new NotFoundException("ProviderConfig", command.ProviderConfigId);

        if (config.IsActive == command.IsActive)
        {
            return AdminMapper.ToDto(config);
        }

        // Tắt chứ không xóa: ví người dùng đã tạo vẫn trỏ tới provider_config_id này. Tắt chỉ
        // gỡ nó khỏi danh sách chọn khi tạo ví MỚI, ví cũ vẫn chạy bình thường.
        config.IsActive = command.IsActive;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await _providerConfigRepository.UpdateAsync(config, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminProviderConfigActivationChanged,
            command.AdminId,
            command.IpAddress,
            new { config.Id, config.ProviderKey, config.IsActive },
            ct);

        return AdminMapper.ToDto(config);
    }
}
