using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.FinancialAccounts.Commands;

public class UpdateFinancialAccountCommandHandler : IUpdateFinancialAccountCommandHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IValidator<UpdateFinancialAccountCommand> _validator;

    public UpdateFinancialAccountCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        IValidator<UpdateFinancialAccountCommand> validator)
    {
        _financialAccountRepository = financialAccountRepository;
        _validator = validator;
    }

    public async Task<FinancialAccountDto> HandleAsync(UpdateFinancialAccountCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var account = await _financialAccountRepository.GetByIdAsync(command.AccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.AccountId);

        account.AccountName = command.AccountName.Trim();
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await _financialAccountRepository.UpdateAsync(account, ct);

        return new FinancialAccountDto(
            account.Id,
            account.AccountType.ToString(),
            account.AccountName,
            account.PackageName,
            account.IsMonitored,
            account.BalanceCents,
            account.ProviderConfig?.DisplayName,
            account.CreatedAt);
    }
}
