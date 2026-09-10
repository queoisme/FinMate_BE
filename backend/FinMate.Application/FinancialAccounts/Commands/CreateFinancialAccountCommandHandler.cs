using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.FinancialAccounts.Commands;

public class CreateFinancialAccountCommandHandler : ICreateFinancialAccountCommandHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IProviderConfigRepository _providerConfigRepository;
    private readonly IValidator<CreateFinancialAccountCommand> _validator;

    public CreateFinancialAccountCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        IProviderConfigRepository providerConfigRepository,
        IValidator<CreateFinancialAccountCommand> validator)
    {
        _financialAccountRepository = financialAccountRepository;
        _providerConfigRepository = providerConfigRepository;
        _validator = validator;
    }

    public async Task<FinancialAccountDto> HandleAsync(CreateFinancialAccountCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        string? packageName = null;
        string? providerDisplayName = null;

        if (command.AccountType == AccountType.Cash)
        {
            if (command.ProviderConfigId is not null)
            {
                throw new BusinessRuleException(
                    FinancialAccountErrorCodes.ProviderInvalid,
                    "Tài khoản tiền mặt không được gắn với nhà cung cấp.");
            }
        }
        else
        {
            if (command.ProviderConfigId is null)
            {
                throw new BusinessRuleException(
                    FinancialAccountErrorCodes.ProviderInvalid,
                    "Vui lòng chọn nhà cung cấp cho tài khoản này.");
            }

            var provider = await _providerConfigRepository.GetByIdAsync(command.ProviderConfigId.Value, ct);
            if (provider is null || !provider.IsActive)
            {
                throw new BusinessRuleException(
                    FinancialAccountErrorCodes.ProviderInvalid,
                    "Nhà cung cấp không tồn tại hoặc không còn khả dụng.");
            }

            packageName = provider.PackageName;
            providerDisplayName = provider.DisplayName;

            var duplicate = await _financialAccountRepository.ExistsByUserAndPackageNameAsync(command.UserId, packageName, ct);
            if (duplicate)
            {
                throw new ConflictException(
                    FinancialAccountErrorCodes.PackageDuplicate,
                    "Bạn đã có tài khoản khác theo dõi ứng dụng này.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var account = new FinancialAccount
        {
            UserId = command.UserId,
            ProviderConfigId = command.AccountType == AccountType.Cash ? null : command.ProviderConfigId,
            AccountType = command.AccountType,
            AccountName = command.AccountName.Trim(),
            PackageName = packageName,
            IsMonitored = true,
            BalanceCents = command.InitialBalanceCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _financialAccountRepository.AddAsync(account, ct);

        return new FinancialAccountDto(
            account.Id,
            account.AccountType.ToString(),
            account.AccountName,
            account.PackageName,
            account.IsMonitored,
            account.BalanceCents,
            providerDisplayName,
            account.CreatedAt);
    }
}
