namespace FinMate.Application.FinancialAccounts.Queries;

public record GetAccountBalanceQuery(Guid UserId, Guid AccountId);
