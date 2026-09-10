namespace FinMate.Application.Common.Models;

public record FinancialAccountDto(
    Guid Id,
    string AccountType,
    string AccountName,
    string? PackageName,
    bool IsMonitored,
    long BalanceCents,
    string? ProviderDisplayName,
    DateTimeOffset CreatedAt);

public record AccountBalanceDto(Guid AccountId, long BalanceCents, DateTimeOffset AsOf);
