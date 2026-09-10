using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.FinancialAccounts.Commands;

public class DeleteFinancialAccountCommandHandler : IDeleteFinancialAccountCommandHandler
{
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ITransactionRepository _transactionRepository;

    public DeleteFinancialAccountCommandHandler(
        IFinancialAccountRepository financialAccountRepository,
        ITransactionRepository transactionRepository)
    {
        _financialAccountRepository = financialAccountRepository;
        _transactionRepository = transactionRepository;
    }

    public async Task HandleAsync(DeleteFinancialAccountCommand command, CancellationToken ct = default)
    {
        var account = await _financialAccountRepository.GetByIdAsync(command.AccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.AccountId);

        if (await _transactionRepository.HasAnyForAccountAsync(account.Id, ct))
        {
            throw new ConflictException(
                FinancialAccountErrorCodes.HasTransactions,
                "Không thể xóa tài khoản đang có giao dịch.");
        }

        var now = DateTimeOffset.UtcNow;
        account.DeletedAt = now;
        account.UpdatedAt = now;

        await _financialAccountRepository.UpdateAsync(account, ct);
    }
}
