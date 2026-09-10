using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

public class GetAccountListQueryHandler : IGetAccountListQueryHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;

    public GetAccountListQueryHandler(IFinancialAccountRepository financialAccountRepository)
    {
        _financialAccountRepository = financialAccountRepository;
    }

    public async Task<IReadOnlyList<FinancialAccountDto>> HandleAsync(GetAccountListQuery query, CancellationToken ct = default)
    {
        var accounts = await _financialAccountRepository.GetListByUserAsync(query.UserId, ct);

        return accounts.Select(a => new FinancialAccountDto(
            a.Id,
            a.AccountType.ToString(),
            a.AccountName,
            a.PackageName,
            a.IsMonitored,
            a.BalanceCents,
            a.ProviderConfig?.DisplayName,
            a.CreatedAt)).ToList();
    }
}
