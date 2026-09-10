using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.FinancialAccounts.Queries;

// balance_cents là pre-computed aggregate (AGENTS.md §3.3) — Phase 4 sẽ cộng/trừ giá trị này
// khi ConfirmTransactionCommand/DeleteTransactionCommand tồn tại. Ở Phase 2 chưa có Transaction
// entity nên chưa có gì để cộng dồn.
public class GetAccountBalanceQueryHandler : IGetAccountBalanceQueryHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;

    public GetAccountBalanceQueryHandler(IFinancialAccountRepository financialAccountRepository)
    {
        _financialAccountRepository = financialAccountRepository;
    }

    public async Task<AccountBalanceDto> HandleAsync(GetAccountBalanceQuery query, CancellationToken ct = default)
    {
        var account = await _financialAccountRepository.GetByIdAsync(query.AccountId, query.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", query.AccountId);

        return new AccountBalanceDto(account.Id, account.BalanceCents, account.UpdatedAt);
    }
}
