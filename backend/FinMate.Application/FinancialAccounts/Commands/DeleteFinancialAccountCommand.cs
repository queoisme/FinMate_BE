namespace FinMate.Application.FinancialAccounts.Commands;

public record DeleteFinancialAccountCommand(Guid UserId, Guid AccountId);
