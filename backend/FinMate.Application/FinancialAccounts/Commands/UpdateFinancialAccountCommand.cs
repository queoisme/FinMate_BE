namespace FinMate.Application.FinancialAccounts.Commands;

public record UpdateFinancialAccountCommand(Guid UserId, Guid AccountId, string AccountName);
