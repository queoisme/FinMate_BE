using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.FinancialAccounts.Commands;

public class DeleteFinancialAccountCommandHandler : IDeleteFinancialAccountCommandHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;

    public DeleteFinancialAccountCommandHandler(IFinancialAccountRepository financialAccountRepository)
    {
        _financialAccountRepository = financialAccountRepository;
    }

    // NOTE: chưa chặn xóa account đang có transactions — Transaction entity thuộc Phase 4,
    // chưa tồn tại ở Phase 2. Xem note "Blocked by Phase 4" trong .context/TASKS.md.
    public async Task HandleAsync(DeleteFinancialAccountCommand command, CancellationToken ct = default)
    {
        var account = await _financialAccountRepository.GetByIdAsync(command.AccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.AccountId);

        var now = DateTimeOffset.UtcNow;
        account.DeletedAt = now;
        account.UpdatedAt = now;

        await _financialAccountRepository.UpdateAsync(account, ct);
    }
}
