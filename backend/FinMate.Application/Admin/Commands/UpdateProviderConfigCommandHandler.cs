using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateProviderConfigCommandHandler : IUpdateProviderConfigCommandHandler
{
    private readonly IProviderConfigRepository _providerConfigRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<UpdateProviderConfigCommand> _validator;

    public UpdateProviderConfigCommandHandler(
        IProviderConfigRepository providerConfigRepository,
        IAuditLogService auditLogService,
        IValidator<UpdateProviderConfigCommand> validator)
    {
        _providerConfigRepository = providerConfigRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<ProviderConfigDto> HandleAsync(
        UpdateProviderConfigCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var config = await _providerConfigRepository.GetByIdAsync(command.ProviderConfigId, ct)
            ?? throw new NotFoundException("ProviderConfig", command.ProviderConfigId);

        if (command.ProviderKey is not null
            && !string.Equals(command.ProviderKey.Trim(), config.ProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField,
                "Không đổi được provider_key: ProviderConfigSeeder và bảng provider_patterns "
                + "của AI Service đều tra theo giá trị này.");
        }

        if (command.PackageName is not null)
        {
            var packageName = command.PackageName.Trim();
            if (packageName != config.PackageName)
            {
                if (await _providerConfigRepository.ExistsByPackageNameAsync(packageName, config.Id, ct))
                {
                    throw new ConflictException(
                        AdminErrorCodes.PackageNameDuplicate,
                        $"Đã có provider khác dùng package '{packageName}'.");
                }

                // Đổi package_name là thao tác chạm sang hệ thống khác: provider_patterns bên
                // AI DB tra regex theo chính giá trị này, và ví người dùng đã tạo vẫn giữ giá
                // trị CŨ trong FinancialAccount.PackageName. Đổi ở đây mà không đồng bộ hai
                // chỗ kia thì thông báo của provider rơi hết về nhánh generic. Ghi vào audit
                // để còn lần ra được.
                config.PackageName = packageName;
            }
        }

        if (command.DisplayName is not null)
        {
            config.DisplayName = command.DisplayName.Trim();
        }

        if (command.AccountType is { } accountType)
        {
            config.AccountType = accountType;
        }

        config.UpdatedAt = DateTimeOffset.UtcNow;
        await _providerConfigRepository.UpdateAsync(config, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminProviderConfigUpdated,
            command.AdminId,
            command.IpAddress,
            new { config.Id, config.ProviderKey, config.DisplayName, config.PackageName, config.AccountType },
            ct);

        return AdminMapper.ToDto(config);
    }
}
