using FinMate.Domain.Enums;

namespace FinMate.Application.FinancialAccounts.Commands;

public record CreateFinancialAccountCommand(
    Guid UserId,
    string AccountName,
    AccountType AccountType,
    Guid? ProviderConfigId,
    long InitialBalanceCents);
