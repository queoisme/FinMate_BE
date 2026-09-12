using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateProviderConfigCommandHandler : ICreateProviderConfigCommandHandler
{
    private readonly IProviderConfigRepository _providerConfigRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<CreateProviderConfigCommand> _validator;

    public CreateProviderConfigCommandHandler(
        IProviderConfigRepository providerConfigRepository,
        IAuditLogService auditLogService,
        IValidator<CreateProviderConfigCommand> validator)
    {
        _providerConfigRepository = providerConfigRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<ProviderConfigDto> HandleAsync(
        CreateProviderConfigCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var providerKey = command.ProviderKey.Trim().ToLowerInvariant();
        var packageName = command.PackageName.Trim();

        if (await _providerConfigRepository.ExistsByProviderKeyAsync(providerKey, ct))
        {
            throw new ConflictException(
                AdminErrorCodes.ProviderKeyDuplicate,
                $"Đã có provider với key '{providerKey}'.");
        }

        // package_name trùng nhau làm GetByUserAndMonitoredPackageAsync không còn xác định
        // được thông báo thuộc về ví nào.
        if (await _providerConfigRepository.ExistsByPackageNameAsync(packageName, null, ct))
        {
            throw new ConflictException(
                AdminErrorCodes.PackageNameDuplicate,
                $"Đã có provider dùng package '{packageName}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var config = new ProviderConfig
        {
            ProviderKey = providerKey,
            DisplayName = command.DisplayName.Trim(),
            PackageName = packageName,
            AccountType = command.AccountType,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _providerConfigRepository.AddAsync(config, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminProviderConfigCreated,
            command.AdminId,
            command.IpAddress,
            new { config.Id, config.ProviderKey, config.PackageName },
            ct);

        return AdminMapper.ToDto(config);
    }
}
