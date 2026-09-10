namespace FinMate.Application.FinancialAccounts.Commands;

public interface IDeleteFinancialAccountCommandHandler
{
    Task HandleAsync(DeleteFinancialAccountCommand command, CancellationToken ct = default);
}
