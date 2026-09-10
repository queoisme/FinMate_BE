using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Commands;

public class ToggleAccountMonitoringCommandHandler : IToggleAccountMonitoringCommandHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;

    public ToggleAccountMonitoringCommandHandler(IFinancialAccountRepository financialAccountRepository)
    {
        _financialAccountRepository = financialAccountRepository;
    }

    public async Task<FinancialAccountDto> HandleAsync(ToggleAccountMonitoringCommand command, CancellationToken ct = default)
    {
        var account = await _financialAccountRepository.GetByIdAsync(command.AccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.AccountId);

        account.IsMonitored = command.IsMonitored;
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
